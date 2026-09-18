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
        Assert.Contains(typeof(StubHttpRequestSnapshot), forwardedTypes);
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

        var emptySnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            emptyResponse,
            cancellationToken: cancellationToken);
        var textSnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            textResponse,
            cancellationToken: cancellationToken);
        var binarySnapshot = await ControllerResponseSnapshot.FromResponseAsync(
            binaryResponse,
            cancellationToken: cancellationToken);

        Assert.Null(emptySnapshot.Body);
        Assert.Equal("café", textSnapshot.Body);
        Assert.Equal(
            new ControllerBinaryBodySnapshot("base64", "AAH/"),
            Assert.IsType<ControllerBinaryBodySnapshot>(binarySnapshot.Body));
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
    public void Controller_snapshot_options_support_fluent_capture_configuration()
    {
        var options = new ControllerSnapshotOptions()
            .WithoutRequest()
            .WithoutBody()
            .WithoutHeaders()
            .IgnoringHeaders("ETag", "X-Correlation-ID")
            .RedactingHeader("X-Session-Token")
            .IncludingHeader("Date");

        Assert.False(options.IncludeRequest);
        Assert.False(options.IncludeBody);
        Assert.False(options.IncludeHeaders);
        Assert.Contains("ETag", options.IgnoredHeaders);
        Assert.Contains("X-Session-Token", options.RedactedHeaders);
        Assert.DoesNotContain("Date", options.IgnoredHeaders);
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
            await File.WriteAllTextAsync(obsoletePath, "{}", cancellationToken);

            var obsolete = catalog.FindObsoleteSnapshots(snapshotDirectory);

            Assert.Single(catalog.ObservedVerifiedSnapshots);
            Assert.Equal(Path.GetFullPath(obsoletePath), Assert.Single(obsolete));
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
        await File.WriteAllTextAsync(firstReceived, "{}", cancellationToken);
        await File.WriteAllTextAsync(secondReceived, "{}", cancellationToken);

        try
        {
            var preview = SnapshotMaintenance.FindReceivedSnapshots(snapshotDirectory);
            Assert.Equal(2, preview.Count);

            Assert.Throws<InvalidOperationException>(() =>
                SnapshotMaintenance.AcceptReceivedSnapshots(snapshotDirectory));
            Assert.All(preview, path => Assert.True(File.Exists(path)));

            var accepted = SnapshotMaintenance.AcceptReceivedSnapshots(
                snapshotDirectory,
                confirmed: true);
            Assert.Equal(2, accepted.Count);
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
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders?notify=true")
            .Respond(HttpStatusCode.Created);
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

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);
            Assert.Contains("\"Method\": \"POST\"", verified);
            Assert.Contains("\"Url\": \"/orders?notify=true\"", verified);
            Assert.Contains("\"orderId\": 42", verified);
            Assert.Contains("\"X-Tenant\"", verified);
            Assert.DoesNotContain("Authorization", verified);
            Assert.DoesNotContain("secret-token", verified);
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
}
