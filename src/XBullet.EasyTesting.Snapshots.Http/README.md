# XBullet.EasyTesting.Snapshots.Http

Snapshot adapters for requests and complete exchanges captured by `XBullet.EasyTesting.Http`. The package references
`XBullet.EasyTesting.Snapshots.Core` and keeps the existing
`XBullet.EasyTesting.Snapshots` namespace.

```shell
dotnet add package XBullet.EasyTesting.Snapshots.Http
```

```csharp
using var response = await client.PostAsJsonAsync("/orders", order);

await handler.ShouldMatchExchangesSnapshot();
```

Exchange snapshots contain each request and its response status, stable headers, and normalized
body. Send failures and response-body read failures are captured as stable exception type/message
pairs. JSON request and response bodies are captured structurally; text remains text and binary
content is stored as base64. Response content that has not been consumed is represented as
`{NotRead}` rather than being read eagerly by the recorder.

Request and response capture can be customized independently:

```csharp
var options = new StubHttpExchangeSnapshotOptions();
options.Request
    .IgnoringHeaders("X-Retry-Count")
    .RedactingHeader("X-Session")
    .RedactingQueryParameter("tenant_secret")
    .ScrubbingUrlPathGuids()
    .ScrubbingQueryParameters("timestamp", "requestId");
options.Response
    .IgnoringHeaders("ETag")
    .RedactingHeader("Set-Cookie");

await handler.ShouldMatchExchangesSnapshot(
    options,
    new SnapshotSettings().ScrubMembers("timestamp", "requestId"));
```

JSON is the default. Complete exchanges can instead use an HTTP-style `.verified.txt` transcript
or deterministic `.verified.yaml` output:

```csharp
var options = new StubHttpExchangeSnapshotOptions
{
    Format = HttpExchangeSnapshotFormat.Yaml // or Http
};

await handler.ShouldMatchExchangesSnapshot(options);
```

Structured JSON scrubbers are applied before either alternative format is rendered.

Request-only snapshots remain available:

```csharp
await handler.ShouldMatchRequestsSnapshot(
    new StubHttpRequestSnapshotOptions()
        .IgnoringHeaders("X-Retry-Count")
        .RedactingHeader("X-Session")
        .RedactingQueryParameter("tenant_secret")
        .ScrubbingUrlPathGuids()
        .ScrubbingQueryParameters("timestamp", "requestId"),
    new SnapshotSettings()
        .ScrubMembers("timestamp", "requestId"));
```

JSON request bodies are captured structurally with `System.Text.Json`. Authorization, cookies,
API keys, correlation IDs, and tracing headers are excluded by default. Common secret-bearing query
parameters are redacted by default; additional header and query values can be preserved as
`{Redacted}`.

Complete GUID path segments are captured as `{Guid}` when `ScrubbingUrlPathGuids()` is enabled.
Use `ScrubbingUrlPath(path => ...)` for other route transformations. Scrubbed query values appear as
`{Scrubbed}`; security-redacted query values appear as `{Redacted}` and take precedence.
