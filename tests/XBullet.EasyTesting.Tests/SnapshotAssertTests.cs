using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using XBullet.EasyTesting.Http;
using XBullet.EasyTesting.Snapshots;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class SnapshotAssertTests
{
    private const string NumericJson =
        """{"large":1234567890123456789012345678901234567890,"precise":0.12345678901234567890123456789,"exponent":1e400}""";

    [Fact]
    public void Compatibility_facade_forwards_to_split_snapshot_assemblies()
    {
        var facade = System.Reflection.Assembly.Load("XBullet.EasyTesting.Snapshots");
        var forwardedTypes = facade.GetForwardedTypes();

        Assert.Contains(typeof(SnapshotAssert), forwardedTypes);
        Assert.Contains(typeof(SnapshotLocationContext), forwardedTypes);
        Assert.Contains(typeof(ControllerSnapshotOptionsDefaults), forwardedTypes);
        Assert.Contains(typeof(StubHttpRequestSnapshot), forwardedTypes);
        Assert.Contains(typeof(StubHttpExchangeSnapshot), forwardedTypes);
        Assert.Contains(typeof(HttpExchangeRecorder), forwardedTypes);
        Assert.Contains(typeof(HttpExchangeFailureSnapshot), forwardedTypes);
        Assert.Contains(typeof(HttpExchangeSnapshotFormat), forwardedTypes);
        Assert.Contains(typeof(HttpExchangeSnapshotOptionsDefaults), forwardedTypes);
        Assert.Equal(
            "XBullet.EasyTesting.Snapshots.Core",
            typeof(SnapshotAssert).Assembly.GetName().Name);
        Assert.Equal(
            "XBullet.EasyTesting.Snapshots.Http",
            typeof(StubHttpRequestSnapshot).Assembly.GetName().Name);

        var coreReferences = typeof(SnapshotAssert).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        Assert.DoesNotContain("XBullet.EasyTesting.Http", coreReferences);
        Assert.DoesNotContain("XBullet.EasyTesting", coreReferences);
    }

    [Fact]
    public async Task Controller_json_body_preserves_number_tokens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                NumericJson,
                Encoding.UTF8,
                "application/problem+json")
        };

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);
        var body = Assert.IsType<JsonElement>(snapshot.Body);

        Assert.Equal(NumericJson, body.GetRawText());
        AssertNumericTokensArePreserved(snapshot);
    }

    [Fact]
    public void Captured_request_json_body_preserves_number_tokens()
    {
        var request = new StubHttpRequest(
            HttpMethod.Post,
            new Uri("https://example.test/orders"),
            new Dictionary<string, string[]>
            {
                ["Content-Type"] = ["application/problem+json; charset=utf-8"]
            },
            NumericJson);

        var snapshot = StubHttpRequestSnapshot.FromRequest(request);
        var body = Assert.IsType<JsonElement>(snapshot.Body);

        Assert.Equal(NumericJson, body.GetRawText());
        AssertNumericTokensArePreserved(snapshot);
    }

    [Fact]
    public void Captured_request_snapshot_redacts_headers_and_sensitive_query_values()
    {
        var request = new StubHttpRequest(
            HttpMethod.Get,
            new Uri("https://example.test/orders?api_key=secret&view=full"),
            new Dictionary<string, string[]>
            {
                ["X-Session"] = ["session-secret"]
            },
            Body: null);
        var options = new StubHttpRequestSnapshotOptions()
            .RedactingHeader("X-Session");

        var snapshot = StubHttpRequestSnapshot.FromRequest(request, options);

        Assert.Equal("/orders?api_key={Redacted}&view=full", snapshot.Url);
        Assert.NotNull(snapshot.Headers);
        Assert.Equal(["{Redacted}"], snapshot.Headers["X-Session"]);
    }

    [Fact]
    public async Task Controller_json_detection_does_not_match_arbitrary_json_suffixes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"value":42}""",
                Encoding.UTF8,
                "application/notjson")
        };

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);

        Assert.IsType<string>(snapshot.Body);
    }

    [Fact]
    public async Task Controller_snapshot_excludes_sensitive_headers_and_supports_redaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=secret");
        response.Headers.TryAddWithoutValidation("Authentication-Info", "auth-secret");
        response.Headers.TryAddWithoutValidation("X-Session-Token", "token-secret");
        var defaultSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);
        var options = new ControllerSnapshotOptions()
            .RedactingHeaders("Set-Cookie", "X-Session-Token");

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        Assert.DoesNotContain("Set-Cookie", defaultSnapshot.Headers);
        Assert.DoesNotContain("Authentication-Info", defaultSnapshot.Headers);
        Assert.Equal(["{Redacted}"], snapshot.Headers["Set-Cookie"]);
        Assert.Equal(["{Redacted}"], snapshot.Headers["X-Session-Token"]);
        Assert.DoesNotContain("Authentication-Info", snapshot.Headers);
        Assert.DoesNotContain(
            snapshot.Headers.SelectMany(header => header.Value),
            value => value.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Controller_snapshot_redacts_sensitive_query_values_and_merges_duplicate_headers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://example.test/orders?access_token=secret&view=full"),
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("X-Combined", "response-value");
        response.Content.Headers.TryAddWithoutValidation("X-Combined", "content-value");

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);

        Assert.Equal("/orders?access_token={Redacted}&view=full", snapshot.Request?.Url);
        Assert.Equal(["response-value", "content-value"], snapshot.Headers["X-Combined"]);
    }

    [Fact]
    public async Task Controller_snapshot_redacts_secret_and_sas_query_parameters_case_insensitively()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://example.test/orders?s%65cret=first&SaS=second&view=full"),
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);

        Assert.Equal(
            "/orders?secret={Redacted}&SaS={Redacted}&view=full",
            snapshot.Request?.Url);
    }

    [Fact]
    public async Task Controller_query_redaction_supports_custom_names_inclusion_and_relative_fragments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("/orders?API%5FKEY=visible&custom=one&custom#section", UriKind.Relative)),
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var options = new ControllerSnapshotOptions()
            .IncludingQueryParameter("API_KEY")
            .RedactingQueryParameter("CUSTOM");

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        Assert.Equal(
            "/orders?API%5FKEY=visible&custom={Redacted}&custom={Redacted}#section",
            snapshot.Request?.Url);
    }

    [Fact]
    public async Task Controller_snapshot_scrubs_guid_routes_and_query_parameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(
                    "/api/system/settings/service-request/f5111111-1111-1111-1111-111111111111" +
                    "?request%5Fid=one&REQUEST_ID&token=secret&view=full#section",
                    UriKind.Relative)),
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var options = new ControllerSnapshotOptions()
            .ScrubbingUrlPathGuids()
            .ScrubbingQueryParameters("Request_Id", "TOKEN");

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        Assert.Equal(
            "/api/system/settings/service-request/{Guid}" +
            "?request%5Fid={Scrubbed}&REQUEST_ID={Scrubbed}&token={Redacted}&view=full#section",
            snapshot.Request?.Url);
    }

    [Fact]
    public async Task Controller_snapshot_can_exclude_all_headers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello", Encoding.UTF8, "text/plain")
        };
        response.Headers.TryAddWithoutValidation("X-Version", "42");

        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            new ControllerSnapshotOptions().WithoutHeaders(),
            cancellationToken);

        Assert.Empty(snapshot.Headers);
    }

    [Fact]
    public async Task Controller_snapshot_handles_empty_text_and_binary_bodies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var emptyResponse = new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent([])
        };
        using var textResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("café", Encoding.Unicode, "text/plain")
        };
        using var binaryResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0, 1, 255])
        };
        binaryResponse.Content.Headers.ContentType = new("application/octet-stream");
        using var charsetResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("café", Encoding.Unicode, "application/x-custom")
        };
        using var untypedBinaryResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0, 1, 255])
        };
        using var utf8TextResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, (Uri?)null),
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("UTF-8 text"))
        };
        utf8TextResponse.Content.Headers.ContentType = new("text/plain");

        var emptySnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            emptyResponse,
            cancellationToken: cancellationToken);
        var textSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            textResponse,
            cancellationToken: cancellationToken);
        var binarySnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            binaryResponse,
            cancellationToken: cancellationToken);
        var charsetSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            charsetResponse,
            cancellationToken: cancellationToken);
        var untypedBinarySnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            untypedBinaryResponse,
            cancellationToken: cancellationToken);
        var utf8TextSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            utf8TextResponse,
            cancellationToken: cancellationToken);

        Assert.Null(emptySnapshot.Body);
        Assert.Equal("café", textSnapshot.Body);
        Assert.Equal("café", charsetSnapshot.Body);
        Assert.Null(utf8TextSnapshot.Request?.Url);
        Assert.Equal("UTF-8 text", utf8TextSnapshot.Body);
        Assert.Equal(
            new ControllerBinaryBodySnapshot("base64", "AAH/"),
            Assert.IsType<ControllerBinaryBodySnapshot>(binarySnapshot.Body));
        Assert.Equal(
            new ControllerBinaryBodySnapshot("base64", "AAH/"),
            Assert.IsType<ControllerBinaryBodySnapshot>(untypedBinarySnapshot.Body));
    }

    [Fact]
    public async Task Controller_snapshot_rejects_malformed_json_body()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{ invalid", Encoding.UTF8, "application/problem+json")
        };

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            ControllerResponseSnapshot.FromResponseAsync(
                response,
                cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task Raw_json_snapshot_preserves_numeric_and_duplicate_property_tokens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("raw-token-fidelity")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();
        var json = $$"""{"number":{{NumericJson}},"value":1,"value":2}""";

        try
        {
            await SnapshotAssert.MatchJsonAsync(json, settings, cancellationToken);

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);

            Assert.Contains("1234567890123456789012345678901234567890", verified);
            Assert.Equal(2, CountOccurrences(verified, "\"value\""));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Invalid_raw_json_does_not_create_snapshot_files()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("invalid-json")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            SnapshotAssert.MatchJsonAsync("{ invalid", settings, cancellationToken));

        Assert.False(Directory.Exists(snapshotDirectory));
    }

    [Fact]
    public async Task Concurrent_matches_to_the_same_snapshot_are_coordinated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            var matches = Enumerable.Range(0, 32)
                .Select(_ => SnapshotAssert.MatchAsync(
                    new { Value = 42 },
                    new SnapshotSettings()
                        .InDirectory(snapshotDirectory)
                        .Named("concurrent")
                        .Updating(SnapshotUpdateMode.Missing)
                        .AllowingUpdatesInContinuousIntegration()
                        .WithoutDiffTool(),
                    cancellationToken));

            await Task.WhenAll(matches);

            Assert.Single(Directory.EnumerateFiles(snapshotDirectory, "*.verified.json"));
            Assert.Empty(Directory.EnumerateFiles(snapshotDirectory, "*.tmp"));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Cancelled_lock_wait_does_not_poison_the_snapshot_path()
    {
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("cancelled-lock");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SnapshotAssert.MatchAsync(new { Value = 1 }, settings, cancellation.Token));

            await SnapshotAssert.MatchAsync(
                new { Value = 1 },
                settings,
                TestContext.Current.CancellationToken);

            Assert.Single(Directory.EnumerateFiles(snapshotDirectory, "*.verified.json"));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Snapshot_variants_create_distinct_files_for_one_test()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Status = "accepted" },
                CreateUpdatingSettings(snapshotDirectory).ForVariant("accepted"),
                cancellationToken);
            await SnapshotAssert.MatchAsync(
                new { Status = "rejected" },
                CreateUpdatingSettings(snapshotDirectory).ForVariant("rejected"),
                cancellationToken);

            var fileNames = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(2, fileNames.Length);
            Assert.Contains(fileNames, name => name!.Contains(".accepted.verified.json", StringComparison.Ordinal));
            Assert.Contains(fileNames, name => name!.Contains(".rejected.verified.json", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Snapshot_names_are_portable_and_escape_without_collisions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Value = 1 },
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("CON")
                    .ForVariant("case:one?two%three. "),
                cancellationToken);
            await SnapshotAssert.MatchAsync(
                new { Value = 2 },
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("CON")
                    .ForVariant("case%003Aone"),
                cancellationToken);

            var fileNames = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
                .Select(Path.GetFileName)
                .ToArray();

            Assert.Equal(2, fileNames.Length);
            Assert.Contains(fileNames, name => name!.Contains(
                ".%0043ON.case%003Aone%003Ftwo%0025three%002E%0020.verified.json",
                StringComparison.Ordinal));
            Assert.Contains(fileNames, name => name!.Contains(
                ".%0043ON.case%0025003Aone.verified.json",
                StringComparison.Ordinal));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Snapshot_names_cover_all_reserved_windows_device_name_forms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var names = new Dictionary<string, string>
        {
            ["PRN"] = "%0050RN",
            ["AUX"] = "%0041UX",
            ["NUL"] = "%004EUL",
            ["COM1"] = "%0043OM1",
            ["LPT9"] = "%004CPT9",
            ["COM0"] = "COM0",
            ["COM10"] = "COM10",
            ["ABC1"] = "ABC1",
            ["LPTA"] = "LPTA"
        };

        try
        {
            foreach (var name in names.Keys)
            {
                await SnapshotAssert.MatchAsync(
                    new { Name = name },
                    CreateUpdatingSettings(snapshotDirectory).Named(name),
                    cancellationToken);
            }

            var fileNames = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
                .Select(Path.GetFileName)
                .ToArray();
            Assert.Equal(names.Count, fileNames.Length);
            foreach (var expectedName in names.Values)
            {
                Assert.Contains(fileNames, fileName => fileName!.Contains(
                    $".{expectedName}.verified.json",
                    StringComparison.Ordinal));
            }
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Case_only_snapshot_path_collisions_fail_clearly_on_every_platform()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Value = 1 },
                CreateUpdatingSettings(snapshotDirectory).Named("CaseSensitiveName"),
                cancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SnapshotAssert.MatchAsync(
                    new { Value = 2 },
                    CreateUpdatingSettings(snapshotDirectory).Named("casesensitivename"),
                    cancellationToken));

            Assert.Contains("case-insensitive file systems", exception.Message);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Json_pointer_rules_target_nested_and_wildcard_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("pointer-rules")
            .ScrubPath("/orders/*/id")
            .IgnorePath("/orders/*/generatedAt")
            .ReplacePath("/status", "stable")
            .ReplacePath("/escaped~1name/~2", "literal-star");

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """
                {
                  "status": "created",
                  "orders": [
                    { "id": 101, "name": "Keyboard", "generatedAt": "2026-01-01T10:00:00Z" },
                    { "id": 102, "name": "Mouse", "generatedAt": "2026-01-01T10:01:00Z" }
                  ],
                  "escaped/name": { "*": "dynamic" }
                }
                """,
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchJsonAsync(
                """
                {
                  "status": "completed",
                  "orders": [
                    { "id": 901, "name": "Keyboard", "generatedAt": "2030-02-02T12:00:00Z" },
                    { "id": 902, "name": "Mouse", "generatedAt": "2030-02-02T12:01:00Z" }
                  ],
                  "escaped/name": { "*": "changed" }
                }
                """,
                settings,
                cancellationToken);

            var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
            Assert.Equal(2, CountOccurrences(verified, "{Scrubbed}"));
            Assert.Contains("\"status\": \"stable\"", verified);
            Assert.Contains("\"*\": \"literal-star\"", verified);
            Assert.DoesNotContain("generatedAt", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Hashed_paths_use_canonical_json_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("hashed-path")
            .HashPath("/payload");

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """{"name":"document","payload":{"b":2,"a":1}}""",
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchJsonAsync(
                """{"name":"document","payload":{"a":1,"b":2}}""",
                settings,
                cancellationToken);

            var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
            Assert.Contains("\"payload\": \"sha256:", verified);
            Assert.DoesNotContain("\"a\": 1", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Canonical_properties_and_sorted_arrays_ignore_incidental_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("canonical-order")
            .SortArray("/items", "/id")
            .CanonicalizeJson();

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """{"z":1,"items":[{"name":"B","id":"2"},{"name":"A","id":"1"}],"a":2}""",
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchJsonAsync(
                """{"a":2,"items":[{"id":"1","name":"A"},{"id":"2","name":"B"}],"z":1}""",
                settings,
                cancellationToken);

            var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
            Assert.True(
                verified.IndexOf("\"a\"", StringComparison.Ordinal) <
                verified.IndexOf("\"items\"", StringComparison.Ordinal));
            Assert.True(
                verified.IndexOf("\"id\": \"1\"", StringComparison.Ordinal) <
                verified.IndexOf("\"id\": \"2\"", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Root_and_array_path_rules_cover_null_and_primitive_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """{"dynamic":42}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("root-replacement")
                    .ReplacePath("", new { Stable = true }),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                "[1,2]",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("array-child-replacement")
                    .ReplacePath("/0", null),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                "null",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("null-root-hash")
                    .HashPath(""),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                "[2,null,1]",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("primitive-root-sort")
                    .SortArray(""),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """["00000000-0000-0000-0000-000000000000","stable",42]""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("array-value-scrubbing")
                    .ScrubGuids(),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """{"a~b":"dynamic"}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("tilde-path-escape")
                    .ReplacePath("/a~0b", "stable"),
                cancellationToken);

            var snapshots = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
                .Select(File.ReadAllText)
                .ToArray();
            Assert.Equal(6, snapshots.Length);
            Assert.Contains(snapshots, snapshot => snapshot.Contains("\"Stable\": true"));
            Assert.Contains(snapshots, snapshot => snapshot.Contains("sha256:"));
            Assert.Contains(snapshots, snapshot => snapshot.Contains("{Guid}"));
            Assert.Contains(snapshots, snapshot => snapshot.Contains("\"a~b\": \"stable\""));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Date_scrubbing_only_matches_round_trip_iso_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("strict-dates")
            .ScrubDateTimes();

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """{"createdAt":"2026-01-01T10:00:00Z","year":"2026","display":"01/02/2026"}""",
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchJsonAsync(
                """{"createdAt":"2030-02-02T12:30:00.1234567+02:00","year":"2026","display":"01/02/2026"}""",
                settings,
                cancellationToken);

            var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
            Assert.Contains("\"createdAt\": \"{DateTime}\"", verified);
            Assert.Contains("\"year\": \"2026\"", verified);
            Assert.Contains("\"display\": \"01/02/2026\"", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Invalid_custom_scrubber_output_is_rejected_before_writing_files()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("invalid-scrubber")
            .Scrub(_ => "{ invalid");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken));

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.False(Directory.Exists(snapshotDirectory));
    }

    [Fact]
    public void Json_pointer_rules_reject_invalid_paths_immediately()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SnapshotSettings().ScrubPath("orders/*/id"));

        Assert.Contains("start with '/'", exception.Message);
    }

    [Fact]
    public async Task Json_content_is_normalized_into_a_json_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("json-content")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """{"name":"Ada","roles":["admin","author"]}""",
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchJsonAsync(
                """
                {
                  "name": "Ada",
                  "roles": [ "admin", "author" ]
                }
                """,
                settings,
                cancellationToken);

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory).Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);

            Assert.EndsWith(".verified.json", verifiedPath, StringComparison.Ordinal);
            Assert.StartsWith("{", verified, StringComparison.Ordinal);
            Assert.Contains("\"name\": \"Ada\"", verified);
            Assert.DoesNotContain("\\\"name\\\"", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Http_json_content_can_be_matched_directly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("http-json-content")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();
        using var content = JsonContent.Create(new { Value = 42 });

        try
        {
            await content.ShouldMatchJsonSnapshot(settings, cancellationToken);

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory).Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);

            Assert.EndsWith(".verified.json", verifiedPath, StringComparison.Ordinal);
            Assert.Contains("\"value\": 42", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Http_response_json_body_can_be_matched_after_content_was_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("http-response-json-body")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Value = 42 })
        };

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using (var reader = new StreamReader(stream, leaveOpen: true))
            {
                _ = await reader.ReadToEndAsync(cancellationToken);
            }

            var originalPosition = stream.Position;

            await response.ShouldMatchJsonBodySnapshot(settings, cancellationToken);

            Assert.Equal(originalPosition, stream.Position);
            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory).Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);
            Assert.Contains("\"value\": 42", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Http_response_json_body_rejects_malformed_content_without_creating_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("malformed-http-response-json")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{ invalid", Encoding.UTF8, "application/problem+json")
        };

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            response.ShouldMatchJsonBodySnapshot(settings, cancellationToken));

        Assert.False(Directory.Exists(snapshotDirectory));
    }

    [Fact]
    public async Task Http_json_content_supports_non_seekable_streams()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("non-seekable-json");
        using var stream = new NonSeekableReadStream(Encoding.UTF8.GetBytes("""{"value":42}"""));
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new("application/json");

        try
        {
            await content.ShouldMatchJsonSnapshot(settings, cancellationToken);

            var verified = await ReadSingleVerifiedSnapshotAsync(
                snapshotDirectory,
                cancellationToken);
            Assert.Contains("\"value\": 42", verified);
            Assert.Equal(stream.Length, stream.BytesRead);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Controller_response_extension_forwards_capture_and_snapshot_options()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(snapshotDirectory, "ControllerExtensionTests.cs");
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("controller-extension");
        using var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders"),
            Content = new StringContent("ignored", Encoding.UTF8, "text/plain")
        };

        try
        {
            await response.ShouldMatchControllerSnapshot(
                new ControllerSnapshotOptions().WithoutBody(),
                settings,
                cancellationToken,
                sourceFile,
                "Controller_extension");

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);
            Assert.Equal("ControllerExtensionTests.controller-extension.verified.json", Path.GetFileName(verifiedPath));
            Assert.Contains("\"StatusCode\": 202", verified);
            Assert.Contains("\"Method\": \"POST\"", verified);
            Assert.DoesNotContain("ignored", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Captured_request_extension_matches_one_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(snapshotDirectory, "RequestExtensionTests.cs");
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("single-request");
        var request = new StubHttpRequest(
            HttpMethod.Put,
            new Uri("https://example.test/orders/42"),
            new Dictionary<string, string[]>(),
            "updated");

        try
        {
            await request.ShouldMatchRequestSnapshot(
                new StubHttpRequestSnapshotOptions().WithoutHeaders(),
                settings,
                cancellationToken,
                sourceFile,
                "Request_extension");

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);
            Assert.Equal("RequestExtensionTests.single-request.verified.json", Path.GetFileName(verifiedPath));
            Assert.Contains("\"Method\": \"PUT\"", verified);
            Assert.Contains("\"Body\": \"updated\"", verified);
            Assert.Contains("\"Headers\": null", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task New_snapshot_writes_a_received_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("new-snapshot")
            .Updating(SnapshotUpdateMode.None)
            .WithoutDiffTool();

        try
        {
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken));

            Assert.True(File.Exists(exception.ReceivedPath));
            Assert.False(File.Exists(exception.VerifiedPath));
            Assert.Matches(@"\.received\.net(8|9|10)\.0\.json$", exception.ReceivedPath);

            var verifiedPath = SnapshotAssert.AcceptReceived(exception.ReceivedPath);

            Assert.Equal(exception.VerifiedPath, verifiedPath);
            Assert.True(File.Exists(verifiedPath));
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Passing_target_does_not_delete_another_runtime_received_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("runtime-received");

        try
        {
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var otherRuntimeReceivedPath = verifiedPath.Replace(
                ".verified.json",
                ".received.net99.0.json",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(otherRuntimeReceivedPath, "different", cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);

            Assert.True(File.Exists(otherRuntimeReceivedPath));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Fully_qualified_snapshot_directory_works_with_sourcelink_caller_path()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("sourcelink-path")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Value = 42 },
                settings,
                cancellationToken,
                sourceFile: "/_/tests/SnapshotAssertTests.cs",
                testName: nameof(Fully_qualified_snapshot_directory_works_with_sourcelink_caller_path));

            Assert.Single(Directory.EnumerateFiles(snapshotDirectory, "*.verified.json"));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Snapshot_can_be_stored_beside_the_calling_source_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sourceDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(sourceDirectory, "LocationTests.cs");
        var settings = new SnapshotSettings()
            .BesideSourceFile()
            .Named("beside-source")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Value = 42 },
                settings,
                cancellationToken,
                sourceFile,
                nameof(Snapshot_can_be_stored_beside_the_calling_source_file));

            Assert.True(File.Exists(Path.Combine(
                sourceDirectory,
                "LocationTests.beside-source.verified.json")));
            Assert.False(Directory.Exists(Path.Combine(sourceDirectory, "__snapshots__")));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(sourceDirectory);
        }
    }

    [Fact]
    public async Task Snapshot_directory_can_be_resolved_from_call_context()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sourceDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(sourceDirectory, "LocationTests.cs");
        SnapshotLocationContext? observedContext = null;
        var settings = new SnapshotSettings()
            .InDirectory(context =>
            {
                observedContext = context;
                return Path.Combine(context.SourceDirectory, "central", context.SnapshotName);
            })
            .Named("resolved")
            .ForVariant("case-one")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Value = 42 },
                settings,
                cancellationToken,
                sourceFile,
                "Resolver_test");

            Assert.NotNull(observedContext);
            Assert.Equal(sourceFile, observedContext.SourceFile);
            Assert.Equal("LocationTests.cs", observedContext.SourceFileName);
            Assert.Equal("Resolver_test", observedContext.TestName);
            Assert.Equal("resolved", observedContext.SnapshotName);
            Assert.Equal("case-one", observedContext.Variant);
            Assert.Single(Directory.EnumerateFiles(
                Path.Combine(sourceDirectory, "central", "resolved"),
                "*.verified.json"));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(sourceDirectory);
        }
    }

    [Fact]
    public async Task Long_snapshot_names_are_shortened_with_a_stable_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named(new string('N', 180))
            .ForVariant(new string('\u00e9', 180));

        try
        {
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);

            var fileName = Path.GetFileName(
                Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single());
            Assert.Contains('~', fileName);
            Assert.True(Encoding.UTF8.GetByteCount(fileName) < 220);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public void Snapshot_variant_can_be_hashed_from_parameter_values()
    {
        var first = new SnapshotSettings().ForHashedVariant("customer", 42).Variant;
        var second = new SnapshotSettings().ForHashedVariant("customer", 42).Variant;
        var different = new SnapshotSettings().ForHashedVariant("customer", 43).Variant;

        Assert.Equal(first, second);
        Assert.StartsWith("hash-", first, StringComparison.Ordinal);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public async Task Plain_text_snapshots_use_text_files_and_custom_scrubbers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("plain-text")
            .Scrub(value => value.Replace("secret", "{Redacted}", StringComparison.Ordinal));

        try
        {
            await SnapshotAssert.MatchTextAsync(
                "first\r\nsecret\r\n",
                settings,
                cancellationToken);
            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.txt").Single();
            Assert.Equal("first\n{Redacted}", await File.ReadAllTextAsync(verifiedPath, cancellationToken));

            settings.Updating(SnapshotUpdateMode.None);
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchTextAsync("second\nsecret", settings, cancellationToken));
            Assert.Matches(@"\.received\.net(8|9|10)\.0\.txt$", exception.ReceivedPath);
            Assert.Equal("$text", exception.DifferencePath);

            Assert.Equal(verifiedPath, SnapshotAssert.AcceptReceived(exception.ReceivedPath));
            await SnapshotAssert.MatchTextAsync("second\nsecret", settings, cancellationToken);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public void Controller_snapshot_options_support_fluent_capture_configuration()
    {
        var options = new ControllerSnapshotOptions()
            .WithoutRequest()
            .WithoutBody()
            .WithoutHeaders()
            .IgnoringHeaders("ETag", "X-Correlation-ID")
            .RedactingHeaders("X-Correlation-ID", "X-Session-Token")
            .IncludingHeader("X-Correlation-ID")
            .RedactingQueryParameters("session", "signature")
            .ScrubbingQueryParameters("volatile", "signature")
            .IncludingQueryParameter("signature");

        Assert.False(options.IncludeRequest);
        Assert.False(options.IncludeBody);
        Assert.False(options.IncludeHeaders);
        Assert.Contains("ETag", options.IgnoredHeaders);
        Assert.Contains("X-Session-Token", options.RedactedHeaders);
        Assert.DoesNotContain("X-Correlation-ID", options.IgnoredHeaders);
        Assert.DoesNotContain("X-Correlation-ID", options.RedactedHeaders);
        Assert.Contains("session", options.RedactedQueryParameters);
        Assert.DoesNotContain("signature", options.RedactedQueryParameters);
        Assert.Contains("volatile", options.ScrubbedQueryParameters);
        Assert.DoesNotContain("signature", options.ScrubbedQueryParameters);
    }

    [Fact]
    public void Captured_request_options_coordinate_header_and_query_rules()
    {
        var options = new StubHttpRequestSnapshotOptions()
            .WithoutHeaders()
            .WithoutBody()
            .IgnoringHeaders("X-First", "X-Second")
            .RedactingHeaders("X-Second", "X-Third")
            .IncludingHeader("X-Third")
            .RedactingQueryParameters("custom", "signature")
            .ScrubbingQueryParameters("volatile", "signature")
            .IncludingQueryParameter("signature");

        Assert.False(options.IncludeHeaders);
        Assert.False(options.IncludeBody);
        Assert.Contains("X-First", options.IgnoredHeaders);
        Assert.DoesNotContain("X-Second", options.IgnoredHeaders);
        Assert.Contains("X-Second", options.RedactedHeaders);
        Assert.DoesNotContain("X-Third", options.RedactedHeaders);
        Assert.Contains("custom", options.RedactedQueryParameters);
        Assert.DoesNotContain("signature", options.RedactedQueryParameters);
        Assert.Contains("volatile", options.ScrubbedQueryParameters);
        Assert.DoesNotContain("signature", options.ScrubbedQueryParameters);
    }

    [Fact]
    public void Captured_request_snapshot_handles_body_and_url_fallbacks()
    {
        var plain = StubHttpRequestSnapshot.FromRequest(new StubHttpRequest(
            HttpMethod.Post,
            new Uri("/plain", UriKind.Relative),
            new Dictionary<string, string[]>(),
            "plain body"));
        var malformedJson = StubHttpRequestSnapshot.FromRequest(new StubHttpRequest(
            HttpMethod.Post,
            new Uri("/json", UriKind.Relative),
            new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            "{ invalid"));
        var invalidContentType = StubHttpRequestSnapshot.FromRequest(new StubHttpRequest(
            HttpMethod.Post,
            null,
            new Dictionary<string, string[]> { ["Content-Type"] = ["not a media type"] },
            "fallback"));
        var withoutBody = StubHttpRequestSnapshot.FromRequest(
            new StubHttpRequest(
                HttpMethod.Post,
                new Uri("/ignored", UriKind.Relative),
                new Dictionary<string, string[]>(),
                "ignored"),
            new StubHttpRequestSnapshotOptions().WithoutBody());

        Assert.Equal("plain body", plain.Body);
        Assert.Equal("{ invalid", malformedJson.Body);
        Assert.Null(invalidContentType.Url);
        Assert.Equal("fallback", invalidContentType.Body);
        Assert.Null(withoutBody.Body);
    }

    [Fact]
    public void Captured_request_query_redaction_supports_custom_names_and_inclusion()
    {
        var request = new StubHttpRequest(
            HttpMethod.Get,
            new Uri("/orders?token=visible&CUSTOM=one&CUSTOM#section", UriKind.Relative),
            new Dictionary<string, string[]>(),
            Body: null);
        var options = new StubHttpRequestSnapshotOptions()
            .IncludingQueryParameter("TOKEN")
            .RedactingQueryParameter("custom");

        var snapshot = StubHttpRequestSnapshot.FromRequest(request, options);

        Assert.Equal(
            "/orders?token=visible&CUSTOM={Redacted}&CUSTOM={Redacted}#section",
            snapshot.Url);
    }

    [Fact]
    public void Captured_request_snapshot_scrubs_custom_routes_and_query_parameters()
    {
        var request = new StubHttpRequest(
            HttpMethod.Get,
            new Uri("/orders/123?timestamp=one&TIMESTAMP#details", UriKind.Relative),
            new Dictionary<string, string[]>(),
            Body: null);
        var options = new StubHttpRequestSnapshotOptions()
            .ScrubbingUrlPath(path => path.Replace("/123", "/{OrderId}", StringComparison.Ordinal))
            .ScrubbingQueryParameter("Timestamp");

        var snapshot = StubHttpRequestSnapshot.FromRequest(request, options);

        Assert.Equal(
            "/orders/{OrderId}?timestamp={Scrubbed}&TIMESTAMP={Scrubbed}#details",
            snapshot.Url);
    }

    [Fact]
    public void Captured_request_snapshot_redacts_secret_and_sas_query_parameters_case_insensitively()
    {
        var request = new StubHttpRequest(
            HttpMethod.Get,
            new Uri("/orders?SeCrEt=first&SAS=second&view=full", UriKind.Relative),
            new Dictionary<string, string[]>(),
            Body: null);

        var snapshot = StubHttpRequestSnapshot.FromRequest(request);

        Assert.Equal(
            "/orders?SeCrEt={Redacted}&SAS={Redacted}&view=full",
            snapshot.Url);
    }

    [Fact]
    public async Task Captured_exchange_snapshot_contains_structural_request_and_response_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders?token=secret")
            .Respond(request => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Headers = { { "X-Session", "secret-session" } },
                Content = JsonContent.Create(new { Accepted = true, request.Body })
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.PostAsJsonAsync(
            "/orders?token=secret",
            new { OrderId = 42 },
            cancellationToken);
        await response.Content.ReadAsStringAsync(cancellationToken);

        var options = new StubHttpExchangeSnapshotOptions();
        options.Response.RedactingHeader("X-Session");
        var snapshot = StubHttpExchangeSnapshot.FromExchange(
            Assert.Single(handler.Exchanges),
            options);

        Assert.Equal("POST", snapshot.Request.Method);
        Assert.Equal("/orders?token={Redacted}", snapshot.Request.Url);
        Assert.Equal(42, Assert.IsType<JsonElement>(snapshot.Request.Body)
            .GetProperty("orderId")
            .GetInt32());
        Assert.Equal((int)HttpStatusCode.Created, snapshot.Response?.StatusCode);
        Assert.NotNull(snapshot.Response?.Headers);
        Assert.Equal(["{Redacted}"], snapshot.Response.Headers["X-Session"]);
        Assert.True(Assert.IsType<JsonElement>(snapshot.Response?.Body)
            .GetProperty("accepted")
            .GetBoolean());
        Assert.Null(snapshot.Failure);
    }

    [Fact]
    public async Task Captured_exchange_snapshot_can_describe_a_response_that_is_still_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/pending")
            .RespondAsync(async (_, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        var send = client.GetAsync("/pending", cancellationToken);

        await entered.Task.WaitAsync(cancellationToken);
        var snapshot = StubHttpExchangeSnapshot.FromExchange(
            Assert.Single(handler.Exchanges));
        release.SetResult();
        using var response = await send;

        Assert.Equal("GET", snapshot.Request.Method);
        Assert.Null(snapshot.Response);
        Assert.Null(snapshot.Failure);
    }

    [Fact]
    public async Task Captured_exchange_snapshot_describes_send_failures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Get, "/failure")
            .Throw(_ => new IOException("connection lost"));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        await Assert.ThrowsAsync<IOException>(() =>
            client.GetAsync("/failure", cancellationToken));
        var snapshot = StubHttpExchangeSnapshot.FromExchange(
            Assert.Single(handler.Exchanges));

        Assert.Null(snapshot.Response);
        Assert.NotNull(snapshot.Failure);
        Assert.Equal(typeof(IOException).FullName, snapshot.Failure.Type);
        Assert.Equal("connection lost", snapshot.Failure.Message);
    }

    [Fact]
    public void Captured_response_options_coordinate_header_rules()
    {
        var options = new StubHttpResponseSnapshotOptions()
            .WithoutHeaders()
            .WithoutBody()
            .IgnoringHeaders("X-First", "X-Second")
            .RedactingHeaders("X-Second", "X-Third")
            .IncludingHeader("X-Third");

        Assert.False(options.IncludeHeaders);
        Assert.False(options.IncludeBody);
        Assert.Contains("X-First", options.IgnoredHeaders);
        Assert.DoesNotContain("X-Second", options.IgnoredHeaders);
        Assert.Contains("X-Second", options.RedactedHeaders);
        Assert.DoesNotContain("X-Third", options.RedactedHeaders);
    }

    [Fact]
    public void Captured_response_snapshot_handles_all_body_content_types()
    {
        var notRead = StubHttpResponseSnapshot.FromResponse(CreateStubResponse(
            bodyCaptured: false,
            body: Encoding.UTF8.GetBytes("not read")));
        var empty = StubHttpResponseSnapshot.FromResponse(CreateStubResponse(
            bodyCaptured: true,
            body: []));
        var malformedJson = StubHttpResponseSnapshot.FromResponse(CreateStubResponse(
            bodyCaptured: true,
            body: Encoding.UTF8.GetBytes("{ invalid"),
            contentTypes: ["invalid content type", "application/problem+json"]));
        var text = StubHttpResponseSnapshot.FromResponse(CreateStubResponse(
            bodyCaptured: true,
            body: Encoding.UTF8.GetBytes("plain text"),
            contentTypes: ["text/plain"]));
        var charsetText = StubHttpResponseSnapshot.FromResponse(CreateStubResponse(
            bodyCaptured: true,
            body: Encoding.Unicode.GetBytes("café"),
            contentTypes: ["application/x-custom; charset=utf-16"]));
        var binary = StubHttpResponseSnapshot.FromResponse(CreateStubResponse(
            bodyCaptured: true,
            body: [0, 1, 255]));
        var excluded = StubHttpResponseSnapshot.FromResponse(
            CreateStubResponse(
                bodyCaptured: true,
                body: Encoding.UTF8.GetBytes("excluded"),
                contentTypes: ["text/plain"]),
            new StubHttpResponseSnapshotOptions().WithoutHeaders().WithoutBody());
        var failed = StubHttpResponseSnapshot.FromResponse(new StubHttpResponse(
            StatusCode: 200,
            ReasonPhrase: "OK",
            Headers: new Dictionary<string, string[]>(),
            BodyCaptured: true,
            Body: ReadOnlyMemory<byte>.Empty,
            BodyFailure: new StubHttpFailure("System.IO.IOException", "read failed")));

        Assert.Equal("{NotRead}", notRead.Body);
        Assert.Null(empty.Body);
        Assert.Equal("{ invalid", malformedJson.Body);
        Assert.Equal("plain text", text.Body);
        Assert.Equal("café", charsetText.Body);
        Assert.Equal(
            new ControllerBinaryBodySnapshot("base64", "AAH/"),
            Assert.IsType<ControllerBinaryBodySnapshot>(binary.Body));
        Assert.Null(excluded.Headers);
        Assert.Null(excluded.Body);
        Assert.Equal("System.IO.IOException", failed.BodyFailure!.Type);
        Assert.Equal("read failed", failed.BodyFailure.Message);
    }

    [Fact]
    public void Http_exchange_options_accept_empty_sets_and_reject_invalid_names()
    {
        var exchange = new HttpExchangeSnapshotOptions();
        var stubExchange = new StubHttpExchangeSnapshotOptions();
        var request = new HttpExchangeRequestSnapshotOptions();
        var response = new HttpExchangeResponseSnapshotOptions();
        var stubResponse = new StubHttpResponseSnapshotOptions();

        Assert.Equal(HttpExchangeSnapshotFormat.Json, exchange.Format);
        Assert.Equal(HttpExchangeSnapshotFormat.Json, stubExchange.Format);
        Assert.Same(request, request.IgnoringHeaders());
        Assert.Same(request, request.RedactingHeaders());
        Assert.Same(request, request.RedactingQueryParameters());
        Assert.Same(request, request.ScrubbingQueryParameters());
        Assert.Same(response, response.IgnoringHeaders());
        Assert.Same(response, response.RedactingHeaders());
        Assert.Same(stubResponse, stubResponse.IgnoringHeaders());
        Assert.Same(stubResponse, stubResponse.RedactingHeaders());
        Assert.Same(request, request.IgnoringHeaders("X-Test"));
        Assert.Contains("X-Test", request.IgnoredHeaders);

        Assert.Throws<ArgumentNullException>(() => request.IgnoringHeaders(null!));
        Assert.Throws<ArgumentException>(() => request.RedactingHeaders(" "));
        Assert.Throws<ArgumentException>(() => request.RedactingQueryParameters(""));
        Assert.Throws<ArgumentException>(() => request.ScrubbingQueryParameters(""));
        Assert.Throws<ArgumentNullException>(() => request.ScrubbingUrlPath(null!));
        Assert.Throws<ArgumentException>(() => request.IncludingHeader(""));
        Assert.Throws<ArgumentException>(() => request.IncludingQueryParameter(" "));
        Assert.Throws<ArgumentNullException>(() => response.RedactingHeaders(null!));
        Assert.Throws<ArgumentException>(() => response.IgnoringHeaders(""));
        Assert.Throws<ArgumentException>(() => response.IncludingHeader(" "));
        Assert.Throws<ArgumentNullException>(() => stubResponse.IgnoringHeaders(null!));
        Assert.Throws<ArgumentException>(() => stubResponse.RedactingHeaders(""));
        Assert.Throws<ArgumentException>(() => stubResponse.IncludingHeader(" "));
    }

    [Fact]
    public async Task Http_response_exchange_snapshot_captures_request_and_response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://orders.example.test/orders?api_key=secret")
        {
            Content = JsonContent.Create(new { OrderId = 42 })
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");
        request.Headers.Add("X-Tenant", "tenant-42");
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            RequestMessage = request,
            Content = JsonContent.Create(new { Id = 42, Status = "accepted" })
        };
        response.Headers.Add("X-Session", "secret-session");

        var options = new HttpExchangeSnapshotOptions();
        options.Request.RedactingHeader("Authorization");
        options.Response.RedactingHeader("X-Session");
        var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        Assert.NotNull(snapshot.Request);
        Assert.NotNull(snapshot.Request.Headers);
        Assert.Equal("POST", snapshot.Request.Method);
        Assert.Equal("/orders?api_key={Redacted}", snapshot.Request.Url);
        Assert.Equal(["{Redacted}"], snapshot.Request.Headers["Authorization"]);
        Assert.Equal(["tenant-42"], snapshot.Request.Headers["X-Tenant"]);
        Assert.Equal(42, Assert.IsType<JsonElement>(snapshot.Request.Body)
            .GetProperty("orderId")
            .GetInt32());
        Assert.NotNull(snapshot.Response);
        Assert.Equal((int)HttpStatusCode.Created, snapshot.Response.StatusCode);
        Assert.NotNull(snapshot.Response.Headers);
        Assert.Equal(["{Redacted}"], snapshot.Response.Headers["X-Session"]);
        Assert.Equal("accepted", Assert.IsType<JsonElement>(snapshot.Response.Body)
            .GetProperty("status")
            .GetString());
    }

    [Fact]
    public async Task Http_response_exchange_snapshot_handles_body_content_types_and_omissions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var emptyResponse = new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent([])
        };
        using var textResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "/text")
            {
                Content = new StringContent("request body", Encoding.UTF8, "text/plain")
            },
            Content = new StringContent("plain text", Encoding.UTF8, "text/plain")
        };
        using var charsetResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("café", Encoding.Unicode, "application/x-custom")
        };
        using var binaryResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, (Uri?)null),
            Content = new ByteArrayContent([0, 1, 255])
        };
        using var utf8TextResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("UTF-8 text"))
        };
        utf8TextResponse.Content.Headers.ContentType = new("text/plain");

        var empty = await HttpExchangeSnapshot.FromResponseAsync(
            emptyResponse,
            cancellationToken: cancellationToken);
        var text = await HttpExchangeSnapshot.FromResponseAsync(
            textResponse,
            cancellationToken: cancellationToken);
        var charsetText = await HttpExchangeSnapshot.FromResponseAsync(
            charsetResponse,
            cancellationToken: cancellationToken);
        var binary = await HttpExchangeSnapshot.FromResponseAsync(
            binaryResponse,
            cancellationToken: cancellationToken);
        var utf8Text = await HttpExchangeSnapshot.FromResponseAsync(
            utf8TextResponse,
            cancellationToken: cancellationToken);
        var omitted = await HttpExchangeSnapshot.FromResponseAsync(
            textResponse,
            new HttpExchangeSnapshotOptions
            {
                Request = { IncludeHeaders = false, IncludeBody = false },
                Response = { IncludeHeaders = false, IncludeBody = false }
            },
            cancellationToken);

        Assert.Null(empty.Response!.Body);
        Assert.Equal("plain text", text.Response!.Body);
        Assert.Equal("café", charsetText.Response!.Body);
        Assert.Equal("UTF-8 text", utf8Text.Response!.Body);
        Assert.Null(binary.Request!.Url);
        Assert.Equal(
            new ControllerBinaryBodySnapshot("base64", "AAH/"),
            Assert.IsType<ControllerBinaryBodySnapshot>(binary.Response!.Body));
        Assert.NotNull(omitted.Request);
        Assert.Null(omitted.Request.Headers);
        Assert.Null(omitted.Request.Body);
        Assert.Null(omitted.Response!.Headers);
        Assert.Null(omitted.Response.Body);
    }

    [Fact]
    public async Task Http_exchange_url_redaction_handles_fragments_flags_and_empty_rules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var flaggedResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("/orders?token&name=visible#details", UriKind.Relative)),
            Content = new ByteArrayContent([])
        };
        using var unredactedResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("/orders?token=visible", UriKind.Relative)),
            Content = new ByteArrayContent([])
        };
        var unredactedOptions = new HttpExchangeSnapshotOptions();
        unredactedOptions.Request.RedactedQueryParameters.Clear();

        var flagged = await HttpExchangeSnapshot.FromResponseAsync(
            flaggedResponse,
            cancellationToken: cancellationToken);
        var unredacted = await HttpExchangeSnapshot.FromResponseAsync(
            unredactedResponse,
            unredactedOptions,
            cancellationToken);

        Assert.Equal("/orders?token={Redacted}&name=visible#details", flagged.Request!.Url);
        Assert.Equal("/orders?token=visible", unredacted.Request!.Url);
    }

    [Fact]
    public async Task Http_exchange_snapshot_redacts_secret_and_sas_query_parameters_case_insensitively()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("/orders?SECRET=first&sAs=second&view=full", UriKind.Relative)),
            Content = new ByteArrayContent([])
        };

        var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);

        Assert.Equal(
            "/orders?SECRET={Redacted}&sAs={Redacted}&view=full",
            snapshot.Request!.Url);
    }

    [Fact]
    public async Task Http_exchange_snapshot_scrubs_guid_routes_and_query_parameters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://example.test/orders/f5111111-1111-1111-1111-111111111111?nonce=one&NONCE=two"),
            Content = new ByteArrayContent([])
        };
        var options = new HttpExchangeSnapshotOptions();
        options.Request
            .ScrubbingUrlPathGuids()
            .ScrubbingQueryParameter("Nonce");

        var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        Assert.Equal(
            "/orders/{Guid}?nonce={Scrubbed}&NONCE={Scrubbed}",
            snapshot.Request!.Url);
    }

    [Fact]
    public async Task Http_exchange_recorder_captures_body_before_transport_consumes_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new HttpExchangeSnapshotOptions();
        options.Response.RedactingHeader("X-Session");
        using var recorder = new HttpExchangeRecorder(options)
        {
            InnerHandler = new CallbackHttpMessageHandler(async (request, token) =>
            {
                _ = await request.Content!.ReadAsStringAsync(token);
                request.Content = null;
                var response = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new { Id = 42, Status = "accepted" }),
                    RequestMessage = request
                };
                response.Headers.Add("X-Session", "secret-session");
                return response;
            })
        };
        using var client = new HttpClient(recorder)
        {
            BaseAddress = new Uri("https://orders.example.test/")
        };
        client.DefaultRequestHeaders.Add("X-Integration-Test-User", "secret-identity");

        using var response = await client.PostAsJsonAsync(
            "/orders",
            new { OrderId = 42 },
            cancellationToken);
        var snapshots = await recorder.CreateSnapshotsAsync(cancellationToken);
        var responseSnapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);

        var snapshot = Assert.Single(snapshots);
        Assert.Same(snapshot, responseSnapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HttpExchangeSnapshot.FromResponseAsync(
                response,
                new HttpExchangeSnapshotOptions(),
                cancellationToken));
        Assert.Equal(1, recorder.CallCount);
        Assert.NotNull(snapshot.Request);
        Assert.DoesNotContain("X-Integration-Test-User", snapshot.Request.Headers!);
        Assert.Equal(42, Assert.IsType<JsonElement>(snapshot.Request.Body)
            .GetProperty("orderId")
            .GetInt32());
        Assert.NotNull(snapshot.Response);
        Assert.Equal((int)HttpStatusCode.Created, snapshot.Response.StatusCode);
        Assert.Equal(["{Redacted}"], snapshot.Response.Headers!["X-Session"]);

        Assert.Same(recorder, recorder.Reset());
        Assert.Equal(0, recorder.CallCount);
    }

    [Fact]
    public async Task Http_exchange_recorder_defers_response_body_capture_until_caller_reads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var content = new GatedHttpContent("streamed body");
        using var recorder = new HttpExchangeRecorder
        {
            InnerHandler = new CallbackHttpMessageHandler((request, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = content
                }))
        };
        using var client = new HttpClient(recorder);

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://orders.example.test/stream"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.False(content.CopyStarted.IsCompleted);
        var unread = Assert.Single(await recorder.CreateSnapshotsAsync(cancellationToken));
        Assert.Equal("{NotRead}", unread.Response!.Body);
        Assert.Null(unread.Response.BodyFailure);

        var read = response.Content.ReadAsStringAsync(cancellationToken);
        await content.CopyStarted.WaitAsync(cancellationToken);
        Assert.False(read.IsCompleted);
        content.Release();
        Assert.Equal("streamed body", await read);

        var captured = Assert.Single(await recorder.CreateSnapshotsAsync(cancellationToken));
        var responseSnapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            cancellationToken: cancellationToken);
        Assert.Equal("streamed body", captured.Response!.Body);
        Assert.Null(captured.Response.BodyFailure);
        Assert.Equal(captured, responseSnapshot);
    }

    [Fact]
    public async Task Http_exchange_recorder_preserves_streaming_response_reads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var source = new GatedReadStream("streamed body");
        using var content = new StreamContent(source);
        content.Headers.ContentType = new("text/plain") { CharSet = "utf-8" };
        content.Headers.ContentLength = source.Length;
        using var recorder = new HttpExchangeRecorder
        {
            InnerHandler = new CallbackHttpMessageHandler((request, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = content
                }))
        };
        using var client = new HttpClient(recorder);

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://orders.example.test/stream"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        Assert.False(source.ReadStarted.IsCompleted);
        var buffer = new byte[checked((int)source.Length)];
        var read = stream.ReadAsync(buffer, cancellationToken).AsTask();
        await source.ReadStarted.WaitAsync(cancellationToken);
        Assert.False(read.IsCompleted);
        source.Release();

        var bytesRead = await read;
        Assert.Equal(source.Length, (long)bytesRead);
        Assert.Equal("streamed body", Encoding.UTF8.GetString(buffer));
        var snapshot = Assert.Single(await recorder.CreateSnapshotsAsync(cancellationToken));
        Assert.Equal("streamed body", snapshot.Response!.Body);
        Assert.Null(snapshot.Response.BodyFailure);
    }

    [Fact]
    public async Task Http_exchange_recorder_preserves_response_when_body_read_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recorder = new HttpExchangeRecorder
        {
            InnerHandler = new CallbackHttpMessageHandler((request, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new FailingHttpContent("partial body", "response body failed")
                }))
        };
        using var client = new HttpClient(recorder);

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://orders.example.test/truncated"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var exception = await Record.ExceptionAsync(
            () => response.Content.ReadAsStringAsync(cancellationToken));
        Assert.NotNull(exception);
        Assert.Contains("response body failed", exception.ToString());

        var snapshot = Assert.Single(await recorder.CreateSnapshotsAsync(cancellationToken));
        Assert.Null(snapshot.Failure);
        Assert.Equal("partial body", snapshot.Response!.Body);
        Assert.Equal(typeof(IOException).FullName, snapshot.Response.BodyFailure!.Type);
        Assert.Equal("response body failed", snapshot.Response.BodyFailure.Message);
    }

    [Fact]
    public async Task Http_exchange_recorder_captures_send_failures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recorder = new HttpExchangeRecorder
        {
            InnerHandler = new CallbackHttpMessageHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("send failed", new IOException("connection lost"))))
        };
        using var client = new HttpClient(recorder)
        {
            BaseAddress = new Uri("https://orders.example.test/")
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("/orders/42", cancellationToken));
        var snapshots = await recorder.CreateSnapshotsAsync(cancellationToken);

        var snapshot = Assert.Single(snapshots);
        Assert.NotNull(snapshot.Request);
        Assert.Equal("/orders/42", snapshot.Request.Url);
        Assert.Null(snapshot.Response);
        Assert.NotNull(snapshot.Failure);
        Assert.Equal(typeof(IOException).FullName, snapshot.Failure.Type);
        Assert.Equal("connection lost", snapshot.Failure.Message);
    }

    [Fact]
    public async Task Http_exchange_recorder_rejects_snapshots_while_a_send_is_in_progress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recorder = new HttpExchangeRecorder
        {
            InnerHandler = new CallbackHttpMessageHandler(async (request, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            })
        };
        using var client = new HttpClient(recorder);
        var send = client.GetAsync("https://orders.example.test/orders", cancellationToken);

        await entered.Task.WaitAsync(cancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.CreateSnapshotsAsync(cancellationToken));
        release.SetResult();
        using var response = await send;

        Assert.Contains("still in progress", exception.Message);
        Assert.Single(await recorder.CreateSnapshotsAsync(cancellationToken));
    }

    [Fact]
    public async Task Snapshot_location_and_acceptance_failures_are_explicit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(snapshotDirectory, "LocationFailureTests.cs");
        var emptyResolver = CreateUpdatingSettings(snapshotDirectory)
            .InDirectory(_ => " ")
            .Named("empty-resolver");
        var emptyName = CreateUpdatingSettings(snapshotDirectory);
        emptyName.SnapshotName = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SnapshotAssert.MatchAsync(new { Value = 1 }, emptyResolver, cancellationToken, sourceFile, "Test"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            SnapshotAssert.MatchAsync(new { Value = 1 }, emptyName, cancellationToken, sourceFile, "Test"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            SnapshotAssert.MatchAsync(new { Value = 1 }, emptyName, cancellationToken, "", "Test"));

        Assert.Throws<ArgumentException>(() =>
            SnapshotAssert.AcceptReceived(Path.Combine(snapshotDirectory, "invalid.json")));
        Assert.Throws<FileNotFoundException>(() =>
            SnapshotAssert.AcceptReceived(Path.Combine(snapshotDirectory, "missing.received.net8.0.json")));
    }

    [Fact]
    public async Task Explicit_snapshot_directory_takes_precedence_over_a_manually_set_resolver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var rootDirectory = CreateTemporarySnapshotDirectory();
        var explicitDirectory = Path.Combine(rootDirectory, "explicit");
        var resolverCalls = 0;
        var settings = new SnapshotSettings
        {
            Directory = explicitDirectory,
            DirectoryResolver = _ =>
            {
                resolverCalls++;
                return Path.Combine(rootDirectory, "resolved");
            },
            SnapshotName = "precedence",
            UpdateMode = SnapshotUpdateMode.Missing,
            AllowUpdatesInContinuousIntegration = true,
            LaunchDiffTool = false
        };

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Value = 42 },
                settings,
                cancellationToken,
                Path.Combine(rootDirectory, "LocationTests.cs"),
                "Precedence");

            Assert.Equal(0, resolverCalls);
            Assert.Single(Directory.EnumerateFiles(explicitDirectory, "*.verified.json"));
            Assert.False(Directory.Exists(Path.Combine(rootDirectory, "resolved")));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task Catalog_and_maintenance_handle_missing_and_invalid_paths_safely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var missingDirectory = Path.Combine(snapshotDirectory, "missing");
        var verifiedPath = Path.Combine(snapshotDirectory, "Keep.verified.json");
        var invalidPath = Path.Combine(snapshotDirectory, "not-a-snapshot.json");
        Directory.CreateDirectory(snapshotDirectory);
        await File.WriteAllTextAsync(verifiedPath, "{}", cancellationToken);
        await File.WriteAllTextAsync(invalidPath, "{}", cancellationToken);

        try
        {
            Assert.Empty(new SnapshotCatalog().FindObsoleteSnapshots(missingDirectory));
            Assert.Empty(SnapshotMaintenance.FindReceivedSnapshots(missingDirectory));
            Assert.Throws<ArgumentException>(() =>
                SnapshotMaintenance.RemoveVerifiedSnapshots(
                    [verifiedPath, invalidPath],
                    confirmed: true));
            Assert.True(File.Exists(verifiedPath));
            Assert.Empty(SnapshotMaintenance.RemoveVerifiedSnapshots(
                [Path.Combine(snapshotDirectory, "Missing.verified.json")],
                confirmed: true));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Automatic_update_modes_create_and_replace_verified_snapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings
        {
            Directory = snapshotDirectory,
            SnapshotName = "automatic-update",
            UpdateMode = SnapshotUpdateMode.Missing,
            AllowUpdatesInContinuousIntegration = true,
            LaunchDiffTool = false
        };

        try
        {
            await SnapshotAssert.MatchAsync(new { Version = 1 }, settings, cancellationToken);

            settings.UpdateMode = SnapshotUpdateMode.None;
            await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken));

            settings.UpdateMode = SnapshotUpdateMode.All;
            await SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken);

            settings.UpdateMode = SnapshotUpdateMode.None;
            await SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Theory]
    [InlineData(null, SnapshotUpdateMode.None)]
    [InlineData("", SnapshotUpdateMode.None)]
    [InlineData("none", SnapshotUpdateMode.None)]
    [InlineData("false", SnapshotUpdateMode.None)]
    [InlineData("0", SnapshotUpdateMode.None)]
    [InlineData("missing", SnapshotUpdateMode.Missing)]
    [InlineData("all", SnapshotUpdateMode.All)]
    [InlineData(" true ", SnapshotUpdateMode.All)]
    [InlineData("1", SnapshotUpdateMode.All)]
    public void Snapshot_update_mode_supports_every_environment_alias(
        string? value,
        SnapshotUpdateMode expected)
    {
        var original = Environment.GetEnvironmentVariable(
            SnapshotSettings.UpdateModeEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                SnapshotSettings.UpdateModeEnvironmentVariable,
                value);

            Assert.Equal(expected, new SnapshotSettings().UpdateMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                SnapshotSettings.UpdateModeEnvironmentVariable,
                original);
        }
    }

    [Fact]
    public void Snapshot_update_mode_rejects_unknown_environment_values()
    {
        var original = Environment.GetEnvironmentVariable(
            SnapshotSettings.UpdateModeEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                SnapshotSettings.UpdateModeEnvironmentVariable,
                "sometimes");

            var exception = Assert.Throws<InvalidOperationException>(() => new SnapshotSettings());
            Assert.Contains(SnapshotSettings.UpdateModeEnvironmentVariable, exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                SnapshotSettings.UpdateModeEnvironmentVariable,
                original);
        }
    }

    [Fact]
    public async Task Mismatch_reports_first_structural_json_path_and_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("diagnostics");

        try
        {
            await SnapshotAssert.MatchAsync(
                new { Order = new { Items = new[] { new { Id = 1, Name = "Keyboard" } } } },
                settings,
                cancellationToken);
            settings.Updating(SnapshotUpdateMode.None);

            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchAsync(
                    new { Order = new { Items = new[] { new { Id = 2, Name = "Keyboard" } } } },
                    settings,
                    cancellationToken));

            Assert.Equal("$.Order.Items[0].Id", exception.DifferencePath);
            Assert.Equal("1", exception.ExpectedValue);
            Assert.Equal("2", exception.ActualValue);
            Assert.Contains("$.Order.Items[0].Id", exception.Message);
            Assert.Contains("expected 1; actual 2", exception.Message);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Mismatch_diagnostics_cover_structural_json_difference_forms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        async Task<SnapshotMismatchException> CompareAsync(
            string name,
            string expected,
            string actual)
        {
            var settings = CreateUpdatingSettings(snapshotDirectory).Named(name);
            await SnapshotAssert.MatchJsonAsync(expected, settings, cancellationToken);
            settings.Updating(SnapshotUpdateMode.None);
            return await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchJsonAsync(actual, settings, cancellationToken));
        }

        try
        {
            var kind = await CompareAsync("kind", "\"text\"", "42");
            var strings = await CompareAsync(
                "strings",
                """{"same":"value","different":"before"}""",
                """{"same":"value","different":"after"}""");
            var numbers = await CompareAsync(
                "numbers",
                """{"same":1,"different":2}""",
                """{"same":1,"different":3}""");
            var propertyName = await CompareAsync(
                "property-name",
                """{"before":1}""",
                """{"after":1}""");
            var missingExpectedProperty = await CompareAsync(
                "missing-expected-property",
                """{"value":1}""",
                """{"value":1,"extra":2}""");
            var missingActualProperty = await CompareAsync(
                "missing-actual-property",
                """{"value":1,"extra":2}""",
                """{"value":1}""");
            var missingExpectedItem = await CompareAsync(
                "missing-expected-item",
                "[true]",
                "[true,false]");
            var missingActualItem = await CompareAsync(
                "missing-actual-item",
                "[null,false]",
                "[null]");
            var escapedProperty = await CompareAsync(
                "escaped-property",
                """{"a-b":1}""",
                """{"a-b":2}""");
            var emptyProperty = await CompareAsync(
                "empty-property",
                """{"":1}""",
                """{"":2}""");
            var underscoreProperty = await CompareAsync(
                "underscore-property",
                """{"_private":1}""",
                """{"_private":2}""");
            var digitProperty = await CompareAsync(
                "digit-property",
                """{"1value":1}""",
                """{"1value":2}""");

            Assert.Equal("$", kind.DifferencePath);
            Assert.Equal("$.different", strings.DifferencePath);
            Assert.Equal("$.different", numbers.DifferencePath);
            Assert.Equal("$", propertyName.DifferencePath);
            Assert.Equal("$.extra", missingExpectedProperty.DifferencePath);
            Assert.Equal("$.extra", missingActualProperty.DifferencePath);
            Assert.Equal("$[1]", missingExpectedItem.DifferencePath);
            Assert.Equal("$[1]", missingActualItem.DifferencePath);
            Assert.Equal("$['a-b']", escapedProperty.DifferencePath);
            Assert.Equal("$['']", emptyProperty.DifferencePath);
            Assert.Equal("$._private", underscoreProperty.DifferencePath);
            Assert.Equal("$['1value']", digitProperty.DifferencePath);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Catalog_detects_verified_snapshots_not_observed_by_the_test_run()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var catalog = new SnapshotCatalog();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("active")
            .TrackingWith(catalog);

        try
        {
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
            var obsoletePath = Path.Combine(snapshotDirectory, "OldTests.Removed.verified.json");
            var obsoleteTextPath = Path.Combine(snapshotDirectory, "OldTests.Removed.verified.txt");
            await File.WriteAllTextAsync(obsoletePath, "{}", cancellationToken);
            await File.WriteAllTextAsync(obsoleteTextPath, "old", cancellationToken);

            var obsolete = catalog.FindObsoleteSnapshots(snapshotDirectory);

            Assert.Single(catalog.ObservedVerifiedSnapshots);
            Assert.Equal(
                [Path.GetFullPath(obsoletePath), Path.GetFullPath(obsoleteTextPath)],
                obsolete);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Bulk_maintenance_requires_preview_and_explicit_confirmation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var nestedDirectory = Path.Combine(snapshotDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        var firstReceived = Path.Combine(snapshotDirectory, "First.received.json");
        var secondReceived = Path.Combine(nestedDirectory, "Second.received.json");
        var thirdReceived = Path.Combine(nestedDirectory, "Third.received.net8.0.txt");
        await File.WriteAllTextAsync(firstReceived, "{}", cancellationToken);
        await File.WriteAllTextAsync(secondReceived, "{}", cancellationToken);
        await File.WriteAllTextAsync(thirdReceived, "text", cancellationToken);

        try
        {
            var preview = SnapshotMaintenance.FindReceivedSnapshots(snapshotDirectory);
            Assert.Equal(3, preview.Count);

            Assert.Throws<InvalidOperationException>(() =>
                SnapshotMaintenance.AcceptReceivedSnapshots(snapshotDirectory));
            Assert.All(preview, path => Assert.True(File.Exists(path)));

            var accepted = SnapshotMaintenance.AcceptReceivedSnapshots(
                snapshotDirectory,
                confirmed: true);
            Assert.Equal(3, accepted.Count);
            Assert.All(accepted, path => Assert.True(File.Exists(path)));

            Assert.Throws<InvalidOperationException>(() =>
                SnapshotMaintenance.RemoveVerifiedSnapshots(accepted));
            var removed = SnapshotMaintenance.RemoveVerifiedSnapshots(
                accepted,
                confirmed: true);

            Assert.Equal(accepted, removed);
            Assert.All(removed, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Automatic_updates_in_ci_require_separate_explicit_opt_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var originalCi = Environment.GetEnvironmentVariable("CI");
        var originalOptIn = Environment.GetEnvironmentVariable(
            SnapshotSettings.AllowCiUpdatesEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable("CI", "true");
            Environment.SetEnvironmentVariable(
                SnapshotSettings.AllowCiUpdatesEnvironmentVariable,
                null);
            var settings = new SnapshotSettings()
                .InDirectory(snapshotDirectory)
                .Named("ci-guard")
                .Updating(SnapshotUpdateMode.Missing)
                .WithoutDiffTool();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken));

            Assert.Contains(
                SnapshotSettings.AllowCiUpdatesEnvironmentVariable,
                exception.Message);
            Assert.Empty(Directory.EnumerateFiles(snapshotDirectory));

            settings.AllowingUpdatesInContinuousIntegration();
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
            Assert.Single(Directory.EnumerateFiles(snapshotDirectory, "*.verified.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", originalCi);
            Environment.SetEnvironmentVariable(
                SnapshotSettings.AllowCiUpdatesEnvironmentVariable,
                originalOptIn);
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Project_defaults_create_independent_settings_with_local_overrides()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var catalog = new SnapshotCatalog();
        var defaults = new SnapshotSettingsDefaults(settings => settings
            .InDirectory(snapshotDirectory)
            .ScrubMember("Id")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .TrackingWith(catalog)
            .WithoutDiffTool());
        var first = defaults.Create(settings => settings.Named("first"));
        var second = defaults.Create(settings => settings.Named("second"));

        try
        {
            first.IgnoreMember("OnlyFirst");
            await SnapshotAssert.MatchAsync(
                new { Id = 123, OnlyFirst = "hidden" },
                first,
                cancellationToken);
            await SnapshotAssert.MatchAsync(
                new { Id = 456, OnlyFirst = "visible" },
                second,
                cancellationToken);

            var snapshots = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
                .Select(File.ReadAllText)
                .ToArray();

            Assert.Equal(2, snapshots.Length);
            Assert.All(snapshots, snapshot => Assert.Contains("{Scrubbed}", snapshot));
            Assert.Contains(snapshots, snapshot => snapshot.Contains("visible", StringComparison.Ordinal));
            Assert.Contains(snapshots, snapshot => !snapshot.Contains("OnlyFirst", StringComparison.Ordinal));
            Assert.Equal(2, catalog.ObservedVerifiedSnapshots.Count);
            Assert.NotSame(first.JsonSerializerOptions, second.JsonSerializerOptions);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Global_defaults_merge_with_explicit_settings_and_local_values_take_precedence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var globalDirectory = CreateTemporarySnapshotDirectory();
        var explicitDirectory = CreateTemporarySnapshotDirectory();
        var originalDefaults = SnapshotSettingsDefaults.Global;

        try
        {
            SnapshotSettingsDefaults.Global = new(settings => settings
                .InDirectory(globalDirectory)
                .Named("global-defaults")
                .ScrubMember("Id")
                .Updating(SnapshotUpdateMode.Missing)
                .AllowingUpdatesInContinuousIntegration()
                .WithoutDiffTool());

            await SnapshotAssert.MatchAsync(
                new { Id = 123, Name = "global" },
                cancellationToken: cancellationToken);
            await SnapshotAssert.MatchAsync(
                new { Id = 456, Timestamp = "volatile", Name = "explicit" },
                new SnapshotSettings()
                    .InDirectory(explicitDirectory)
                    .Named("explicit-settings")
                    .ScrubMember("Timestamp"),
                cancellationToken);

            var globalSnapshot = await ReadSingleVerifiedSnapshotAsync(
                globalDirectory,
                cancellationToken);
            var explicitSnapshot = await ReadSingleVerifiedSnapshotAsync(
                explicitDirectory,
                cancellationToken);

            Assert.Contains("{Scrubbed}", globalSnapshot);
            Assert.DoesNotContain("123", globalSnapshot);
            Assert.Contains("{Scrubbed}", explicitSnapshot);
            Assert.DoesNotContain("456", explicitSnapshot);
            Assert.DoesNotContain("volatile", explicitSnapshot);
        }
        finally
        {
            SnapshotSettingsDefaults.Global = originalDefaults;
            DeleteTemporarySnapshotDirectory(globalDirectory);
            DeleteTemporarySnapshotDirectory(explicitDirectory);
        }
    }

    [Fact]
    public async Task ExtendGlobal_copies_global_defaults_and_applies_local_configuration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var originalDefaults = SnapshotSettingsDefaults.Global;

        try
        {
            SnapshotSettingsDefaults.Global = new(settings => settings
                .InDirectory(snapshotDirectory)
                .Named("extended-global-defaults")
                .ScrubMember("Id")
                .Updating(SnapshotUpdateMode.Missing)
                .AllowingUpdatesInContinuousIntegration()
                .WithoutDiffTool());

            var settings = SnapshotSettingsDefaults.ExtendGlobal(settings => settings
                .ScrubMember("Timestamp"));

            await SnapshotAssert.MatchAsync(
                new { Id = 123, Timestamp = "volatile", Name = "Keyboard" },
                settings,
                cancellationToken);

            var snapshot = await ReadSingleVerifiedSnapshotAsync(
                snapshotDirectory,
                cancellationToken);
            Assert.DoesNotContain("123", snapshot);
            Assert.DoesNotContain("volatile", snapshot);
            Assert.Contains("Keyboard", snapshot);
        }
        finally
        {
            SnapshotSettingsDefaults.Global = originalDefaults;
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Global_controller_options_merge_with_local_options_and_local_decisions_win()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalDefaults = ControllerSnapshotOptionsDefaults.Global;

        static HttpResponseMessage CreateResponse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    "/api/products/f5111111-1111-1111-1111-111111111111" +
                    "?global_secret=one&local_secret=two&GLOBAL_NONCE=three&local_nonce=four"),
                Content = JsonContent.Create(new { Value = 42 })
            };
            response.Headers.TryAddWithoutValidation("X-Global-Ignored", "ignored");
            response.Headers.TryAddWithoutValidation("X-Global-Redacted", "visible-locally");
            response.Headers.TryAddWithoutValidation("X-Local-Ignored", "ignored");
            return response;
        }

        try
        {
            ControllerSnapshotOptionsDefaults.Global = new(options => options
                .WithoutRequest()
                .WithoutBody()
                .IgnoringHeaders("X-Global-Ignored")
                .RedactingHeader("X-Global-Redacted")
                .RedactingQueryParameter("global_secret")
                .ScrubbingQueryParameter("global_nonce")
                .ScrubbingUrlPathGuids()
                .ScrubbingUrlPath(path =>
                    path.Replace("/api", "/root", StringComparison.Ordinal)));

            using var defaultResponse = CreateResponse();
            var defaultSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
                defaultResponse,
                cancellationToken: cancellationToken);
            Assert.Null(defaultSnapshot.Request);
            Assert.Null(defaultSnapshot.Body);
            Assert.DoesNotContain("X-Global-Ignored", defaultSnapshot.Headers);
            Assert.Equal(["{Redacted}"], defaultSnapshot.Headers["X-Global-Redacted"]);

            using var localResponse = CreateResponse();
            var localOptions = new ControllerSnapshotOptions
            {
                IncludeRequest = true,
                IncludeBody = true
            }
                .IncludingHeader("X-Global-Redacted")
                .IgnoringHeaders("X-Local-Ignored")
                .RedactingQueryParameter("local_secret")
                .ScrubbingQueryParameter("LOCAL_NONCE")
                .ScrubbingUrlPath(path =>
                    path.Replace("/products", "/items", StringComparison.Ordinal));
            var localSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
                localResponse,
                localOptions,
                cancellationToken);

            Assert.Equal(
                "/root/items/{Guid}?global_secret={Redacted}&local_secret={Redacted}" +
                "&GLOBAL_NONCE={Scrubbed}&local_nonce={Scrubbed}",
                localSnapshot.Request?.Url);
            Assert.NotNull(localSnapshot.Body);
            Assert.DoesNotContain("X-Global-Ignored", localSnapshot.Headers);
            Assert.DoesNotContain("X-Local-Ignored", localSnapshot.Headers);
            Assert.Equal(["visible-locally"], localSnapshot.Headers["X-Global-Redacted"]);
        }
        finally
        {
            ControllerSnapshotOptionsDefaults.Global = originalDefaults;
        }
    }

    [Fact]
    public async Task Global_defaults_apply_to_parameterless_http_exchange_assertions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var originalDefaults = SnapshotSettingsDefaults.Global;

        try
        {
            SnapshotSettingsDefaults.Global = new(settings => settings
                .InDirectory(snapshotDirectory)
                .Named("global-http-exchange")
                .ScrubMember("Id")
                .Updating(SnapshotUpdateMode.Missing)
                .AllowingUpdatesInContinuousIntegration()
                .WithoutDiffTool());
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "/api/products/42"),
                Content = JsonContent.Create(new { Id = 42, Name = "Keyboard" })
            };

            await response.ShouldMatchHttpExchangeSnapshot(
                cancellationToken: cancellationToken);

            var snapshot = await ReadSingleVerifiedSnapshotAsync(
                snapshotDirectory,
                cancellationToken);
            Assert.Contains("/api/products/42", snapshot);
            Assert.Contains("{Scrubbed}", snapshot);
            Assert.DoesNotContain("\"id\": 42", snapshot);
        }
        finally
        {
            SnapshotSettingsDefaults.Global = originalDefaults;
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public void Http_exchange_option_defaults_merge_local_decisions_and_copy_independently()
    {
        var originalDefaults = HttpExchangeSnapshotOptionsDefaults.Global;

        try
        {
            var defaults = new HttpExchangeSnapshotOptionsDefaults(options =>
            {
                options.Format = HttpExchangeSnapshotFormat.Yaml;
                options.Request
                    .WithoutBody()
                    .IgnoringHeaders("X-Global-Ignored")
                    .RedactingHeader("X-Global-Redacted")
                    .RedactingQueryParameter("global_secret")
                    .ScrubbingQueryParameter("global_nonce")
                    .ScrubbingUrlPath(path => path.Replace("/api", "/root", StringComparison.Ordinal));
                options.Response
                    .WithoutHeaders()
                    .IgnoringHeaders("X-Response-Ignored")
                    .RedactingHeader("X-Response-Redacted");
            });
            HttpExchangeSnapshotOptionsDefaults.Global = defaults;

            var first = defaults.Create();
            var second = defaults.Create();
            first.Request.IgnoredHeaders.Add("X-Only-First");

            Assert.DoesNotContain("X-Only-First", second.Request.IgnoredHeaders);

            var local = new HttpExchangeSnapshotOptions
            {
                Format = HttpExchangeSnapshotFormat.Http
            };
            local.Request.IncludeBody = true;
            local.Request
                .IncludingHeader("X-Global-Ignored")
                .IgnoringHeaders("X-Local-Ignored")
                .IncludingQueryParameter("global_secret")
                .ScrubbingQueryParameter("local_nonce")
                .ScrubbingUrlPath(path => path.Replace("/products", "/items", StringComparison.Ordinal));
            local.Response.IncludeHeaders = true;
            local.Response
                .IncludingHeader("X-Response-Ignored")
                .IgnoringHeaders("X-Local-Response");

            var merged = HttpExchangeSnapshotOptionsDefaults.MergeGlobalOrDefault(local);

            Assert.Equal(HttpExchangeSnapshotFormat.Http, merged.Format);
            Assert.True(merged.Request.IncludeBody);
            Assert.DoesNotContain("X-Global-Ignored", merged.Request.IgnoredHeaders);
            Assert.Contains("X-Local-Ignored", merged.Request.IgnoredHeaders);
            Assert.Contains("X-Global-Redacted", merged.Request.RedactedHeaders);
            Assert.DoesNotContain("global_secret", merged.Request.RedactedQueryParameters);
            Assert.Contains("global_nonce", merged.Request.ScrubbedQueryParameters);
            Assert.Contains("local_nonce", merged.Request.ScrubbedQueryParameters);
            Assert.True(merged.Response.IncludeHeaders);
            Assert.DoesNotContain("X-Response-Ignored", merged.Response.IgnoredHeaders);
            Assert.Contains("X-Local-Response", merged.Response.IgnoredHeaders);
            Assert.Contains("X-Response-Redacted", merged.Response.RedactedHeaders);

            var extended = HttpExchangeSnapshotOptionsDefaults.ExtendGlobal(options =>
                options.Request.ScrubbingUrlPathGuids());
            Assert.Equal(HttpExchangeSnapshotFormat.Yaml, extended.Format);
            Assert.False(extended.Request.IncludeBody);
            Assert.Equal(2, extended.Request.UrlPathScrubbers.Count);
            Assert.Same(
                extended,
                HttpExchangeSnapshotOptionsDefaults.MergeGlobalOrDefault(extended));
        }
        finally
        {
            HttpExchangeSnapshotOptionsDefaults.Global = originalDefaults;
        }
    }

    [Fact]
    public async Task Http_exchange_global_options_apply_to_direct_captures_and_recorders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalDefaults = HttpExchangeSnapshotOptionsDefaults.Global;

        try
        {
            HttpExchangeSnapshotOptionsDefaults.Global = new(options =>
            {
                options.Format = HttpExchangeSnapshotFormat.Yaml;
                options.Request.WithoutHeaders();
                options.Response.WithoutBody();
            });
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "/orders"),
                Content = JsonContent.Create(new { Id = 42 })
            };

            var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
                response,
                cancellationToken: cancellationToken);
            using var recorder = new HttpExchangeRecorder();
            var local = new HttpExchangeSnapshotOptions
            {
                Format = HttpExchangeSnapshotFormat.Http
            };
            using var localRecorder = new HttpExchangeRecorder(local)
            {
                InnerHandler = new CallbackHttpMessageHandler((request, _) => Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.Accepted)
                    {
                        RequestMessage = request
                    }))
            };
            using var client = new HttpClient(localRecorder);
            using var recordedResponse = await client.GetAsync(
                "https://orders.example.test/orders/42",
                cancellationToken);
            var recordedSnapshot = await HttpExchangeSnapshot.FromResponseAsync(
                recordedResponse,
                local,
                cancellationToken);

            Assert.Null(snapshot.Request!.Headers);
            Assert.Null(snapshot.Response!.Body);
            Assert.Equal(HttpExchangeSnapshotFormat.Yaml, recorder.Options.Format);
            Assert.False(recorder.Options.Request.IncludeHeaders);
            Assert.False(recorder.Options.Response.IncludeBody);
            Assert.Equal((int)HttpStatusCode.Accepted, recordedSnapshot.Response!.StatusCode);
            Assert.Equal(
                HttpExchangeSnapshotFormat.Http,
                HttpExchangeRecorder.ResolveFormat(recordedResponse, local));
            Assert.Throws<InvalidOperationException>(() =>
                HttpExchangeRecorder.ResolveFormat(
                    recordedResponse,
                    new HttpExchangeSnapshotOptions()));
        }
        finally
        {
            HttpExchangeSnapshotOptionsDefaults.Global = originalDefaults;
        }
    }

    [Fact]
    public async Task Http_exchange_assertion_can_store_an_http_transcript()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("http-transcript")
            .ScrubMember("id");
        var options = new HttpExchangeSnapshotOptions
        {
            Format = HttpExchangeSnapshotFormat.Http
        };
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            ReasonPhrase = "Created",
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new { Id = 42, Name = "Keyboard" })
            },
            Content = JsonContent.Create(new { Id = 43, Accepted = true })
        };

        try
        {
            await response.ShouldMatchHttpExchangeSnapshot(
                options,
                settings,
                cancellationToken);

            var path = Directory.EnumerateFiles(snapshotDirectory, "*.verified.txt").Single();
            var snapshot = await File.ReadAllTextAsync(path, cancellationToken);
            Assert.Contains("=== Request ===", snapshot);
            Assert.Contains("POST /api/orders", snapshot);
            Assert.Contains("Content-Type: application/json; charset=utf-8", snapshot);
            Assert.Contains("=== Response ===", snapshot);
            Assert.Contains("201 Created", snapshot);
            Assert.Contains("\"id\": \"{Scrubbed}\"", snapshot);
            Assert.DoesNotContain("\"Request\":", snapshot);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Recorded_http_exchange_uses_the_recorders_yaml_format()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory).Named("recorded-yaml");
        var options = new HttpExchangeSnapshotOptions
        {
            Format = HttpExchangeSnapshotFormat.Yaml
        };
        using var recorder = new HttpExchangeRecorder(options)
        {
            InnerHandler = new CallbackHttpMessageHandler((request, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("hello\nworld", Encoding.UTF8, "text/plain")
                }))
        };
        using var client = new HttpClient(recorder);

        try
        {
            using var response = await client.GetAsync(
                "https://orders.example.test/orders/42",
                cancellationToken);
            _ = await response.Content.ReadAsStringAsync(cancellationToken);
            await response.ShouldMatchHttpExchangeSnapshot(
                snapshotSettings: settings,
                cancellationToken: cancellationToken);

            var path = Directory.EnumerateFiles(snapshotDirectory, "*.verified.yaml").Single();
            var snapshot = await File.ReadAllTextAsync(path, cancellationToken);
            Assert.Contains("Request:", snapshot);
            Assert.Contains("Method: \"GET\"", snapshot);
            Assert.Contains("Url: \"/orders/42\"", snapshot);
            Assert.Contains("Response:", snapshot);
            Assert.Contains("StatusCode: 200", snapshot);
            Assert.Contains("Body: \"hello\\nworld\"", snapshot);
            Assert.DoesNotContain("=== Request ===", snapshot);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public void Http_exchange_text_formatter_covers_collections_failures_and_body_kinds()
    {
        const string exchanges = """
            [
              {
                "Request": null,
                "Response": null,
                "Failure": { "Type": "System.IO.IOException", "Message": "send failed\nnow" }
              },
              {
                "Request": {
                  "Method": null,
                  "Url": null,
                  "Headers": null,
                  "Body": { "Encoding": "base64", "Value": "AAH/" }
                },
                "Response": {
                  "Headers": {
                    "X-Empty": [],
                    "X-Invalid": "not-an-array",
                    "X-Lines": ["first\r\nsecond", null]
                  },
                  "Body": "plain text\n",
                  "BodyFailure": { "Type": "System.IO.IOException", "Message": "read failed" }
                },
                "Failure": null
              },
              {
                "Request": {
                  "Method": "POST",
                  "Url": "/json",
                  "Headers": { "Content-Type": ["application/problem+json"] },
                  "Body": "json string"
                },
                "Response": {
                  "StatusCode": 200,
                  "ReasonPhrase": "",
                  "Headers": { "Content-Type": ["application/octet-stream"] },
                  "Body": { "Encoding": "base64", "Value": "AQI=" },
                  "BodyFailure": 42
                },
                "Failure": 42
              }
            ]
            """;

        var formatted = HttpExchangeSnapshotFormatter
            .Format(exchanges, HttpExchangeSnapshotFormat.Http)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("=== Exchange 1 ===", formatted);
        Assert.Contains("=== Exchange 3 ===", formatted);
        Assert.Contains("<unknown-method> <unknown-url>", formatted);
        Assert.Contains("# Headers: <excluded>", formatted);
        Assert.Contains("# Body encoding: base64\nAAH/", formatted);
        Assert.Contains("X-Empty:", formatted);
        Assert.Contains("X-Invalid:", formatted);
        Assert.Contains("X-Lines: first\\r\\nsecond", formatted);
        Assert.Contains("plain text", formatted);
        Assert.Contains("=== Response Body Failure ===", formatted);
        Assert.Contains("\"json string\"", formatted);
        Assert.Contains("# Body encoding: base64\nAQI=", formatted);
        Assert.Contains("=== Failure ===", formatted);
        Assert.Contains("Type: <unknown>", formatted);
        Assert.Contains("Message: \"\"", formatted);
        Assert.Equal(
            "=== Exchanges ===\n<none>",
            HttpExchangeSnapshotFormatter.Format("[]", HttpExchangeSnapshotFormat.Http));
        Assert.Throws<InvalidOperationException>(() =>
            HttpExchangeSnapshotFormatter.Format("42", HttpExchangeSnapshotFormat.Http));
    }

    [Fact]
    public void Http_exchange_yaml_formatter_emits_a_deterministic_json_compatible_subset()
    {
        const string json = """
            {
              "safe-key": {
                "items": [true, false, null, 1.25, "text", {}, []],
                "emptyObject": {},
                "emptyArray": []
              },
              "1 unsafe:key": "quoted key",
              "null": "quoted keyword"
            }
            """;

        var yaml = HttpExchangeSnapshotFormatter.Format(json, HttpExchangeSnapshotFormat.Yaml);

        Assert.Contains("safe-key:", yaml);
        Assert.Contains("- true", yaml);
        Assert.Contains("- false", yaml);
        Assert.Contains("- null", yaml);
        Assert.Contains("- 1.25", yaml);
        Assert.Contains("- \"text\"", yaml);
        Assert.Contains("- {}", yaml);
        Assert.Contains("- []", yaml);
        Assert.Contains("emptyObject: {}", yaml);
        Assert.Contains("emptyArray: []", yaml);
        Assert.Contains("\"1 unsafe:key\": \"quoted key\"", yaml);
        Assert.Contains("\"null\": \"quoted keyword\"", yaml);
        Assert.Equal("true", HttpExchangeSnapshotFormatter.Format("true", HttpExchangeSnapshotFormat.Yaml));
        Assert.Equal("{}", HttpExchangeSnapshotFormatter.Format("{}", HttpExchangeSnapshotFormat.Yaml));
        Assert.Equal("[]", HttpExchangeSnapshotFormatter.Format("[]", HttpExchangeSnapshotFormat.Yaml));
        Assert.Equal(json, HttpExchangeSnapshotFormatter.Format(json, HttpExchangeSnapshotFormat.Json));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HttpExchangeSnapshotFormatter.Format(json, (HttpExchangeSnapshotFormat)42));
    }

    [Fact]
    public async Task Global_defaults_apply_to_json_text_and_http_content_assertions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var originalDefaults = SnapshotSettingsDefaults.Global;

        SnapshotSettingsDefaults CreateDefaults(string name) => new(settings => settings
            .InDirectory(snapshotDirectory)
            .Named(name)
            .Scrub(serialized => serialized)
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool());

        try
        {
            SnapshotSettingsDefaults.Global = CreateDefaults("global-json");
            await SnapshotAssert.MatchJsonAsync(
                """{"value":42}""",
                cancellationToken: cancellationToken);

            SnapshotSettingsDefaults.Global = CreateDefaults("global-text");
            await SnapshotAssert.MatchTextAsync(
                "plain text",
                cancellationToken: cancellationToken);

            SnapshotSettingsDefaults.Global = CreateDefaults("global-http-content");
            using var content = JsonContent.Create(new { Value = 43 });
            await content.ShouldMatchJsonSnapshot(cancellationToken: cancellationToken);

            Assert.Equal(
                3,
                Directory.EnumerateFiles(snapshotDirectory, "*.verified.*").Count());
        }
        finally
        {
            SnapshotSettingsDefaults.Global = originalDefaults;
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Structured_scrubbing_stabilizes_dynamic_values_and_ignored_members()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("structured-scrubbing")
            .ScrubMembers("RequestId", "CreatedAt")
            .IgnoreMembers("AccessToken")
            .ScrubGuids()
            .ScrubDateTimes()
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

        try
        {
            await SnapshotAssert.MatchAsync(
                new
                {
                    RequestId = 123,
                    CreatedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
                    AccessToken = "secret-one",
                    Metadata = new
                    {
                        CorrelationId = Guid.Parse("3c91271a-a927-4516-b225-41cce45963b5"),
                        ExpiresAt = "2026-01-01T11:00:00Z"
                    }
                },
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchAsync(
                new
                {
                    RequestId = 999,
                    CreatedAt = new DateTimeOffset(2030, 2, 2, 12, 0, 0, TimeSpan.Zero),
                    AccessToken = "secret-two",
                    Metadata = new
                    {
                        CorrelationId = Guid.Parse("637717e4-10d3-4302-bff2-846b17f00591"),
                        ExpiresAt = "2030-02-02T13:00:00Z"
                    }
                },
                settings,
                cancellationToken);

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);

            Assert.Contains("\"RequestId\": \"{Scrubbed}\"", verified);
            Assert.Contains("\"CorrelationId\": \"{Guid}\"", verified);
            Assert.Contains("\"ExpiresAt\": \"{DateTime}\"", verified);
            Assert.DoesNotContain("AccessToken", verified);
            Assert.DoesNotContain("secret-one", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Theory]
    [InlineData("different-kinds", "{}", "[]", "$")]
    [InlineData("different-property", "{\"first\":1}", "{\"second\":1}", "$")]
    [InlineData("missing-property", "{\"first\":1,\"second\":2}", "{\"first\":1}", "$.second")]
    [InlineData("extra-property", "{\"first\":1}", "{\"first\":1,\"second\":2}", "$.second")]
    [InlineData("missing-array-item", "[1,2]", "[1]", "$[1]")]
    [InlineData("extra-array-item", "[1]", "[1,2]", "$[1]")]
    [InlineData("escaped-property", "{\"first.name\":1}", "{\"first.name\":2}", "$['first.name']")]
    [InlineData("invalid-expected", "{ invalid", "{\"first\":1}", "$")]
    public async Task Mismatch_diagnostics_cover_structural_difference_shapes(
        string snapshotName,
        string expected,
        string actual,
        string expectedPath)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(snapshotDirectory, "Diagnostics.cs");
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named(snapshotName)
            .WithoutDiffTool();
        Directory.CreateDirectory(snapshotDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(snapshotDirectory, $"Diagnostics.{snapshotName}.verified.json"),
            expected,
            cancellationToken);

        try
        {
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchJsonAsync(
                    actual,
                    settings,
                    cancellationToken,
                    sourceFile,
                    "Diagnostics"));

            Assert.Equal(expectedPath, exception.DifferencePath);
            Assert.NotNull(exception.ExpectedValue);
            Assert.NotNull(exception.ActualValue);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Mismatch_diagnostics_escape_property_names_and_truncate_long_values()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(snapshotDirectory, "Diagnostics.cs");
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("escaped-and-long")
            .WithoutDiffTool();
        var expectedValue = new string('a', 250);
        var actualValue = new string('b', 250);
        Directory.CreateDirectory(snapshotDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(snapshotDirectory, "Diagnostics.escaped-and-long.verified.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["quote'\\name"] = expectedValue }),
            cancellationToken);

        try
        {
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchJsonAsync(
                    JsonSerializer.Serialize(
                        new Dictionary<string, string> { ["quote'\\name"] = actualValue }),
                    settings,
                    cancellationToken,
                    sourceFile,
                    "Diagnostics"));

            Assert.Equal("$['quote\\'\\\\name']", exception.DifferencePath);
            Assert.EndsWith("...", exception.ExpectedValue, StringComparison.Ordinal);
            Assert.EndsWith("...", exception.ActualValue, StringComparison.Ordinal);
            Assert.Equal(203, exception.ExpectedValue?.Length);
            Assert.Equal(203, exception.ActualValue?.Length);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Structured_rules_support_root_object_wildcard_and_array_index_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            await SnapshotAssert.MatchJsonAsync(
                """{"dynamic":42}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("root-replacement")
                    .ReplacePath("", new { Stable = true }),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """{"dynamic":42}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("root-removal")
                    .IgnorePath(""),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """{"first":{"secret":"one"},"second":{"secret":"two"}}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("object-wildcard")
                    .ScrubPath("/*/secret"),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """{"items":["first","remove","last"]}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("array-index")
                    .IgnorePath("/items/1"),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """{"items":[null]}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("null-descent")
                    .ScrubPath("/items/*/value"),
                cancellationToken);
            await SnapshotAssert.MatchJsonAsync(
                """{"value":42}""",
                CreateUpdatingSettings(snapshotDirectory)
                    .Named("primitive-descent")
                    .ScrubPath("/value/child"),
                cancellationToken);

            var snapshots = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
                .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);
            Assert.Contains("\"Stable\": true", snapshots["SnapshotAssertTests.root-replacement.verified.json"]);
            Assert.Equal("null", snapshots["SnapshotAssertTests.root-removal.verified.json"]);
            Assert.Equal(2, CountOccurrences(
                snapshots["SnapshotAssertTests.object-wildcard.verified.json"],
                "{Scrubbed}"));
            Assert.DoesNotContain("remove", snapshots["SnapshotAssertTests.array-index.verified.json"]);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Structured_rules_reject_invalid_sort_targets_and_path_escapes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SnapshotAssert.MatchJsonAsync(
                    """{"value":42}""",
                    CreateUpdatingSettings(snapshotDirectory)
                        .Named("sort-non-array")
                        .SortArray("/value"),
                    cancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SnapshotAssert.MatchJsonAsync(
                    """{"items":[{"name":"missing id"}]}""",
                    CreateUpdatingSettings(snapshotDirectory)
                        .Named("missing-sort-key")
                        .SortArray("/items", "/id"),
                    cancellationToken));

            Assert.Throws<ArgumentException>(() =>
                new SnapshotSettings().SortArray("/items", "/*/id"));
            Assert.Throws<ArgumentException>(() =>
                new SnapshotSettings().ScrubPath("/invalid~"));
            Assert.Throws<ArgumentException>(() =>
                new SnapshotSettings().ScrubPath("/invalid~3escape"));
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public void Diff_tool_configuration_validates_and_builds_supported_commands()
    {
        Assert.Throws<ArgumentException>(() => new SnapshotDiffTool(" "));
        Assert.Throws<ArgumentNullException>(() =>
            new SnapshotDiffTool("custom", (string[])null!));

        var custom = new SnapshotDiffTool("custom", "--left", "{verified}", "{received}");
        var settings = new SnapshotSettings().WithDiffTool(custom);
        var visualStudio = SnapshotDiffTool.VisualStudio("devenv-custom");
        var visualStudioCode = SnapshotDiffTool.VisualStudioCode("code-custom");
        var rider = SnapshotDiffTool.Rider("rider-custom");

        Assert.True(settings.LaunchDiffTool);
        Assert.Same(custom, settings.DiffTool);
        Assert.Equal("custom", custom.Executable);
        Assert.Equal(["--left", "{verified}", "{received}"], custom.Arguments);
        Assert.Equal("devenv-custom", visualStudio.Executable);
        Assert.Equal(["/diff", "{verified}", "{received}"], visualStudio.Arguments);
        Assert.Equal("code-custom", visualStudioCode.Executable);
        Assert.Equal(["--diff", "{verified}", "{received}"], visualStudioCode.Arguments);
        Assert.Equal("rider-custom", rider.Executable);
        Assert.Equal(["diff", "{verified}", "{received}"], rider.Arguments);
        Assert.Throws<ArgumentNullException>(() => new SnapshotSettings().WithDiffTool(null!));
    }

    [Fact]
    public async Task Captured_http_requests_have_a_dedicated_snapshot_assertion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("outbound-requests")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();
        var exchangeSettings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("outbound-exchanges")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders?notify=true")
            .RespondJson(new { Accepted = true }, HttpStatusCode.Created);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret-token");
        client.DefaultRequestHeaders.Add("X-Tenant", "tenant-42");

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/orders?notify=true",
                new { OrderId = 42 },
                cancellationToken);

            await handler.ShouldMatchRequestsSnapshot(
                snapshotSettings: settings,
                cancellationToken: cancellationToken);
            await handler.ShouldMatchExchangesSnapshot(
                snapshotSettings: exchangeSettings,
                cancellationToken: cancellationToken);

            var verifiedPaths = Directory
                .EnumerateFiles(snapshotDirectory, "*.verified.json")
                .ToArray();
            var requestsVerified = await File.ReadAllTextAsync(
                verifiedPaths.Single(path => path.Contains("outbound-requests")),
                cancellationToken);
            var exchangesVerified = await File.ReadAllTextAsync(
                verifiedPaths.Single(path => path.Contains("outbound-exchanges")),
                cancellationToken);
            Assert.Contains("\"Method\": \"POST\"", requestsVerified);
            Assert.Contains("\"Url\": \"/orders?notify=true\"", requestsVerified);
            Assert.Contains("\"orderId\": 42", requestsVerified);
            Assert.Contains("\"X-Tenant\"", requestsVerified);
            Assert.DoesNotContain("Authorization", requestsVerified);
            Assert.DoesNotContain("secret-token", requestsVerified);
            Assert.Contains("\"Response\"", exchangesVerified);
            Assert.Contains("\"StatusCode\": 201", exchangesVerified);
            Assert.Contains("\"accepted\": true", exchangesVerified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Stub_http_exchanges_can_store_yaml_snapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = CreateUpdatingSettings(snapshotDirectory)
            .Named("stub-yaml")
            .ScrubMember("orderId");
        var options = new StubHttpExchangeSnapshotOptions
        {
            Format = HttpExchangeSnapshotFormat.Yaml
        };
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders")
            .RespondJson(new { OrderId = 42 }, HttpStatusCode.Created);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/orders",
                new { OrderId = 41 },
                cancellationToken);
            await handler.ShouldMatchExchangesSnapshot(
                options,
                settings,
                cancellationToken);

            var path = Directory.EnumerateFiles(snapshotDirectory, "*.verified.yaml").Single();
            var snapshot = await File.ReadAllTextAsync(path, cancellationToken);
            Assert.StartsWith("-\n  Request:", snapshot);
            Assert.Contains("StatusCode: 201", snapshot);
            Assert.Contains("orderId: \"{Scrubbed}\"", snapshot);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Semantically_equal_json_reports_the_root_when_text_formatting_differs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(snapshotDirectory, "Diagnostics.cs");
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("formatting-only")
            .WithoutDiffTool();
        Directory.CreateDirectory(snapshotDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(snapshotDirectory, "Diagnostics.formatting-only.verified.json"),
            """{"items":[1,2]}""",
            cancellationToken);

        try
        {
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchJsonAsync(
                    """{ "items": [1, 2] }""",
                    settings,
                    cancellationToken,
                    sourceFile,
                    "Diagnostics"));

            Assert.Equal("$", exception.DifferencePath);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Parameterless_text_snapshot_uses_default_directory_and_reports_missing_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sourceDirectory = CreateTemporarySnapshotDirectory();
        var sourceFile = Path.Combine(sourceDirectory, "DefaultDirectory.cs");
        var originalDefaults = SnapshotSettingsDefaults.Global;

        try
        {
            SnapshotSettingsDefaults.Global = null;
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchTextAsync(
                    "plain text",
                    cancellationToken: cancellationToken,
                    sourceFile: sourceFile,
                    testName: "MissingText"));

            Assert.Equal("$text", exception.DifferencePath);
            Assert.Contains(
                Path.Combine(sourceDirectory, "__snapshots__"),
                exception.ReceivedPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SnapshotSettingsDefaults.Global = originalDefaults;
            DeleteTemporarySnapshotDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void Snapshot_path_lock_release_is_idempotent()
    {
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var path = Path.Combine(snapshotDirectory, "idempotent.verified.json");

        try
        {
            var pathLock = SnapshotPathLock.Acquire(path);
            pathLock.Dispose();
            pathLock.Dispose();
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    private static string CreateTemporarySnapshotDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            "XBullet.EasyTesting.Tests",
            Guid.NewGuid().ToString("N"));

    private static SnapshotSettings CreateUpdatingSettings(string snapshotDirectory) =>
        new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool();

    private static StubHttpResponse CreateStubResponse(
        bool bodyCaptured,
        byte[] body,
        string[]? contentTypes = null) =>
        new(
            StatusCode: 200,
            ReasonPhrase: "OK",
            Headers: contentTypes is null
                ? new Dictionary<string, string[]>()
                : new Dictionary<string, string[]> { ["Content-Type"] = contentTypes },
            BodyCaptured: bodyCaptured,
            Body: body,
            BodyFailure: null);

    private static async Task<string> ReadSingleVerifiedSnapshotAsync(
        string snapshotDirectory,
        CancellationToken cancellationToken)
    {
        var path = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static void AssertNumericTokensArePreserved(object snapshot)
    {
        var json = JsonSerializer.Serialize(
            snapshot,
            new SnapshotSettings().JsonSerializerOptions);

        Assert.Contains("1234567890123456789012345678901234567890", json);
        Assert.Contains("0.12345678901234567890123456789", json);
        Assert.Contains("1e400", json);
    }

    private static int CountOccurrences(string value, string searchValue) =>
        value.Split(searchValue, StringSplitOptions.None).Length - 1;

    private static void DeleteTemporarySnapshotDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class NonSeekableReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);

        public long BytesRead => _inner.Position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class GatedReadStream : Stream
    {
        private readonly byte[] _content;
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public GatedReadStream(string content) => _content = Encoding.UTF8.GetBytes(content);

        public Task ReadStarted => _readStarted.Task;

        public void Release() => _release.TrySetResult();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _readStarted.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            var bytesRead = Math.Min(count, _content.Length - _position);
            _content.AsSpan(_position, bytesRead).CopyTo(buffer.AsSpan(offset, bytesRead));
            _position += bytesRead;
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            var bytesRead = Math.Min(buffer.Length, _content.Length - _position);
            _content.AsMemory(_position, bytesRead).CopyTo(buffer);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class GatedHttpContent : HttpContent
    {
        private readonly byte[] _content;
        private readonly TaskCompletionSource _copyStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedHttpContent(string content)
        {
            _content = Encoding.UTF8.GetBytes(content);
            Headers.ContentType = new("text/plain") { CharSet = "utf-8" };
        }

        public Task CopyStarted => _copyStarted.Task;

        public void Release() => _release.TrySetResult();

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context) =>
            SerializeCoreAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken) =>
            SerializeCoreAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = _content.Length;
            return true;
        }

        private async Task SerializeCoreAsync(Stream stream, CancellationToken cancellationToken)
        {
            _copyStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            await stream.WriteAsync(_content, cancellationToken);
        }
    }

    private sealed class FailingHttpContent : HttpContent
    {
        private readonly byte[] _content;
        private readonly string _message;

        public FailingHttpContent(string content, string message)
        {
            _content = Encoding.UTF8.GetBytes(content);
            _message = message;
            Headers.ContentType = new("text/plain") { CharSet = "utf-8" };
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context) =>
            SerializeCoreAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken) =>
            SerializeCoreAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private async Task SerializeCoreAsync(Stream stream, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(_content, cancellationToken);
            throw new IOException(_message);
        }
    }

    private sealed class CallbackHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
}
