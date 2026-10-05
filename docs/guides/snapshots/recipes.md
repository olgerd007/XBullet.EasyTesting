# Snapshot recipes

Install `XBullet.EasyTesting.Snapshots.Core` for values, JSON, text, controller responses, and real
TestServer exchanges. Add `XBullet.EasyTesting.Snapshots.Http` only for outbound requests captured
by `StubHttpMessageHandler`.

## Values and raw JSON

```csharp
await SnapshotAssert.MatchAsync(result, settings, cancellationToken);
await SnapshotAssert.MatchJsonAsync(rawJson, settings, cancellationToken);
```

`MatchAsync` serializes with `System.Text.Json`. `MatchJsonAsync` parses and normalizes a raw JSON
string instead of storing it as an escaped string. For HTTP content or response bodies:

```csharp
await response.Content.ShouldMatchJsonSnapshot(settings, cancellationToken);
await response.ShouldMatchJsonBodySnapshot(settings, cancellationToken);
```

Use body-only assertions when request metadata, status, and headers are outside the contract.

## Plain text

`MatchTextAsync` preserves text rather than parsing JSON and creates `.txt` files:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-plain-text -->
```csharp
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
```
<!-- end-snippet -->

String scrubbers apply to text snapshots. Prefer structural rules for JSON so formatting changes do
not hide or create false differences.

## Controller responses

`ShouldMatchControllerSnapshot` captures a real response with optional request, status, headers,
and body controls:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-controller-response -->
```csharp
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
```
<!-- end-snippet -->

Use `ControllerSnapshotOptions` to omit the request or body, ignore unstable headers, redact
sensitive header and query values, and scrub volatile URL segments.

## Complete TestServer exchanges

`ShouldMatchHttpExchangeSnapshot` supports both direct capture from a response and exchanges
already captured by an `HttpExchangeRecorder`. Configure exchange options at the point of capture.

### Choose where to configure exchange options

| Response source | When capture happens | Where to configure exchange options |
| --- | --- | --- |
| Client without `HttpExchangeRecorder` | During the assertion, from `response` and `response.RequestMessage` | `configureExchange` or `exchangeOptions` on `ShouldMatchHttpExchangeSnapshot` |
| Client with `HttpExchangeRecorder`, including `SnapshotScenario(...)` | Request before transport; response when received and body when consumed | Recorder or scenario/client creation, before sending the request |

For direct capture, the assertion can configure headers, bodies, redaction, URL scrubbing, and
output format:

```csharp
using var response = await client.GetAsync($"/api/action/{id}", cancellationToken);

await response.ShouldMatchHttpExchangeSnapshot(
    configureExchange: options => options.Request.ScrubbingUrlPathGuids(),
    cancellationToken: cancellationToken);
```

This example requires a client without an `HttpExchangeRecorder`. Direct capture reads the data
still available during assertion; it cannot recover request content already consumed by transport
or record a send failure that produced no response.

**For a recorded response, passing `configureExchange` to the assertion throws
`InvalidOperationException`.** The callback creates a separate options instance, which the recorder
guard rejects even if it only adds URL scrubbing or supplies values identical to the capture
options. The message starts with
`This response was captured by an HttpExchangeRecorder with different options.`
The object overload also rejects a separate `exchangeOptions` instance; it accepts the original
instance supplied to the recorder or `recorder.Options`. Passing that same instance during assertion
does not recapture the exchange. Prefer omitting exchange options from recorded-response assertions.

`configureSnapshot` and `snapshotSettings` work for both response sources. They transform the
snapshot after capture and control naming, storage, comparison, and updates.

### Configure recorder-backed exchange capture

Attach an `HttpExchangeRecorder` as a delegating handler when the request body must be captured
before transport consumes it. The recorder observes the real TestServer pipeline. The example
factory's `SnapshotClient(...)` helper attaches this handler:

<!-- snippet: tests/TestApi.IntegrationTests/CrudControllerSnapshotTests.cs#docs-snapshots-complete-exchange -->
```csharp
[Fact]
public Task Create_matches_custom_snapshot_with_generated_id_scrubbed() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = _factory.SnapshotClient(
                scope,
                options => options.Response.IgnoringHeaders("Location"))
            .AsUser(user => user.WithName("CRUD snapshot tester"))
            .Build();

        using var response = await client.PostAsJsonAsync(
            "/api/products",
            new ProductRequest("Webcam", 79.95m),
            cancellationToken);

        await response.ShouldMatchHttpExchangeSnapshot(
            snapshotSettings: BuiltInSnapshotAudit.CreateSettings(
                settings => settings.ScrubMember("id")),
            cancellationToken: cancellationToken);
    });
```
<!-- end-snippet -->

The `XBullet.EasyTesting.Snapshots.Http` package can also attach the recorder directly to a fluent
scenario. This is required when the request body belongs in the exchange snapshot because the
server may consume it before the response is asserted:

```csharp
using var result = await scope.SnapshotScenario(options =>
    {
        options.Request.ScrubbingUrlPathGuids();
        options.Response.IgnoringHeaders("Location");
    })
    .PostJson("/api/products", new ProductRequest("Webcam", 79.95m))
    .ExecuteAsync(cancellationToken);

await result.Response.ShouldMatchHttpExchangeSnapshot(
    configureSnapshot: settings => settings.ScrubMember("id"),
    cancellationToken: cancellationToken);
```

For GUID URL scrubbing during assertion, use `SnapshotSettings` with either response source:

```csharp
await response.ShouldMatchHttpExchangeSnapshot(
    configureSnapshot: settings => settings.ScrubbingUrlPathGuids(),
    cancellationToken: cancellationToken);
```

See [HTTP redaction and URL stability](stabilizing-data.md#http-redaction-and-url-stability) for
selective GUID scrubbing. This settings rule leaves the recorded exchange and its capture options
unchanged.

JSON is the default. Set `HttpExchangeSnapshotOptions.Format` to `Http` for a transcript or `Yaml`
for deterministic YAML, at the capture configuration point shown above. With a recorder, an unread
streaming response body is represented as `{NotRead}`; content-read errors are recorded as
`BodyFailure`, and send failures are captured separately.

## Outbound HTTP stub requests and exchanges

Install the adapter package:

```shell
dotnet add package XBullet.EasyTesting.Snapshots.Http
```

Snapshot one recorded request, all requests, or complete request/response exchanges:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-outbound-http -->
```csharp
[Fact]
public async Task Captured_http_requests_have_a_dedicated_snapshot_assertion()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
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
            configureSnapshot: settings => settings
                .InDirectory(snapshotDirectory)
                .Named("outbound-requests")
                .Updating(SnapshotUpdateMode.Missing)
                .AllowingUpdatesInContinuousIntegration()
                .WithoutDiffTool(),
            cancellationToken: cancellationToken);
        await handler.ShouldMatchExchangesSnapshot(
            configureSnapshot: settings => settings
                .InDirectory(snapshotDirectory)
                .Named("outbound-exchanges")
                .Updating(SnapshotUpdateMode.Missing)
                .AllowingUpdatesInContinuousIntegration()
                .WithoutDiffTool(),
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
        Assert.DoesNotContain("\"Failure\"", exchangesVerified);
        Assert.DoesNotContain("\"BodyFailure\"", exchangesVerified);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Authentication, cookies, common API-key headers, correlation IDs, and tracing headers are excluded
by default. Configure request and response capture independently. Outbound exchange snapshots can
also use JSON, HTTP transcript, or YAML formats.

## Sensitive complete exchange

This realistic test redacts a request header, scrubs nested personal data, and replaces a dynamic
route before writing the exchange:

<!-- snippet: tests/TestApi.IntegrationTests/CrudControllerSnapshotTests.cs#docs-snapshots-redacted-exchange -->
```csharp
[Fact]
public Task Update_matches_full_exchange_with_nested_request_and_path_scrubbing() =>
    Run(async (scope, cancellationToken) =>
    {
        await _factory.Database(scope)
            .Seed(new Product { Id = 922, Name = "Before", Price = 10m })
            .ExecuteAsync(cancellationToken);
        using var client = _factory.SnapshotClient(
                scope,
                options => options.Request.RedactingHeader("X-Request-Secret"))
            .AsUser(user => user.WithName("CRUD snapshot tester"))
            .WithHeader("X-Request-Secret", "request-secret-value")
            .Build();

        using var response = await client.PutAsJsonAsync(
            "/api/products/922",
            new
            {
                Name = "Updated with nested metadata",
                Price = 27.50m,
                Customer = new
                {
                    Id = 701,
                    Contact = new
                    {
                        Email = "private@example.test",
                        Locale = "en-US"
                    }
                }
            },
            cancellationToken);

        var settings = BuiltInSnapshotAudit.CreateSettings(settings => settings
            .ScrubMember("email")
            .ScrubPath("/Request/Body/customer/id")
            .ReplacePath("/Request/Url", "/api/products/{id}"));

        await response.ShouldMatchHttpExchangeSnapshot(
            snapshotSettings: settings,
            cancellationToken: cancellationToken);
    });
```
<!-- end-snippet -->

Next: [stabilize dynamic and sensitive data](stabilizing-data.md) and configure
[shared defaults](defaults.md).

API references: [snapshot core](../../api/packages/xbullet-easytesting-snapshots-core.md) and
[outbound HTTP adapters](../../api/packages/xbullet-easytesting-snapshots-http.md).
