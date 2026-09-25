# XBullet.EasyTesting.Snapshots.Core

Lightweight, framework-independent JSON and text snapshot assertions for HTTP responses and
arbitrary values. This package does not depend on `XBullet.EasyTesting`, ASP.NET testing, or
`XBullet.EasyTesting.Http`.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Snapshots.Core
```

Use `XBullet.EasyTesting.Snapshots.Http` for snapshots of outbound requests captured by
`StubHttpMessageHandler`. The original `XBullet.EasyTesting.Snapshots` package remains available as
a compatibility facade that references both packages.

```csharp
var settings = new SnapshotSettings()
    .Named("administrator-order")
    .ScrubMembers("Id", "CreatedAt")
    .ScrubGuids();

await SnapshotAssert.MatchAsync(result, settings);
```

For a centralized or test-specific layout, resolve the directory from the calling context:

```csharp
var settings = new SnapshotSettings()
    .InDirectory(context => Path.Combine(
        context.SourceDirectory,
        "snapshots",
        context.SourceFileName));
```

### Snapshot locations

Snapshots are stored in a `__snapshots__` directory beside the calling source file by default.
Choose another directory with `InDirectory`; relative paths are resolved from the calling source
file rather than the process working directory. To keep snapshots directly beside the source file,
use `BesideSourceFile`:

```csharp
var settings = new SnapshotSettings()
    .BesideSourceFile();

await SnapshotAssert.MatchAsync(result, settings);
```

### Raw JSON

Raw JSON can be verified as structured JSON instead of as an escaped string. Raw strings and
HTTP content are parsed and normalized with `System.Text.Json`. Verified files use
`*.verified.json`; received files include the current target framework, such as
`*.received.net8.0.json`, so multi-targeted test runs cannot overwrite each other's failures.

```csharp
var json = $$"""
    {
      "orderId": 42,
      "status": "ready",
      "correlationId": "{{Guid.NewGuid()}}"
    }
    """;
var settings = new SnapshotSettings()
    .ScrubGuids();

await SnapshotAssert.MatchJsonAsync(json, settings);
```

The resulting verified snapshot contains normalized JSON:

```json
{
  "orderId": 42,
  "status": "ready",
  "correlationId": "{Guid}"
}
```

Invalid JSON throws `JsonException` without creating a snapshot.

### Plain text

Use `MatchTextAsync` when the content should not be parsed or serialized as JSON. Text snapshots
use `.verified.txt` and runtime-qualified `.received.*.txt` files. Custom string scrubbers still
apply:

```csharp
var settings = new SnapshotSettings()
    .Scrub(text => text.Replace(secret, "{Redacted}", StringComparison.Ordinal));

await SnapshotAssert.MatchTextAsync(commandOutput, settings);
```

### HTTP JSON content

Verify only the JSON response body when status, headers, and request metadata do not belong in the
snapshot:

```csharp
using var response = await client.GetAsync("/api/orders/42");
response.EnsureSuccessStatusCode();

await response.ShouldMatchJsonBodySnapshot();
```

`response.Content.ShouldMatchJsonSnapshot()` is also available when only the `HttpContent` is in
scope. For buffered or seekable content, both assertions rewind the body and restore its original
position, so they remain reliable after the body has already been read.

Use `response.ShouldMatchControllerSnapshot()` instead when the snapshot should also contain the
request method and URL, response status, and stable headers. Sensitive and volatile headers,
including `Set-Cookie`, `Authentication-Info`, `Proxy-Authentication-Info`, `Date`, and tracing
identifiers, are excluded by default. Header capture can be customized without exposing values:

```csharp
var options = new ControllerSnapshotOptions()
    .RedactingHeaders("Set-Cookie", "X-Session-Token")
    .RedactingQueryParameter("tenant_secret");

await response.ShouldMatchControllerSnapshot(options);
```

Redacted headers and query values are captured as `{Redacted}`. Common secret-bearing query names,
including `access_token`, `api_key`, `client_secret`, `sas`, `secret`, `sig`, and `token`, are
redacted by default.
Call `WithoutHeaders()` to omit the entire header collection, or `IncludingHeader(name)` and
`IncludingQueryParameter(name)` to explicitly include a value known to be safe.

### Complete HTTP exchanges

Use `HttpExchangeRecorder` when a snapshot must contain the complete request and response,
including the request body. It is a delegating handler, not a stub, so the request still reaches
the real server or ASP.NET Core TestServer. With XBullet's test-client builder:

```csharp
var exchangeOptions = new HttpExchangeSnapshotOptions();
exchangeOptions.Response.IgnoringHeaders("Location");

var recorder = new HttpExchangeRecorder(exchangeOptions);
using var client = scope.Client()
    .AsUser(user => user.WithName("snapshot tester"))
    .WithHandler(recorder)
    .Build();

using var response = await client.PostAsJsonAsync(
    "/api/products",
    new { name = "Webcam", price = 79.95m });

await response.ShouldMatchHttpExchangeSnapshot(
    snapshotSettings: new SnapshotSettings().ScrubMember("id"));
```

The recorder captures requests before transport consumption, captures response metadata before
returning it, and records response content as the caller reads it. This preserves
`ResponseHeadersRead` and streaming behavior. An unread response body appears as `{NotRead}`, while
a content-read error is recorded as `BodyFailure` without being misclassified as a send failure.
JSON is stored structurally; text stays text; binary bodies use base64. Authorization, cookies, API
keys, XBullet's test identity and client-certificate transport headers, correlation identifiers,
and tracing headers are excluded by default. Configure request and response headers independently
through `HttpExchangeSnapshotOptions`. A test factory or client helper can attach a fresh recorder
to each client, so individual tests only need the response extension shown above.

Without a recorder, the same response extension falls back to `HttpResponseMessage.RequestMessage`.
That is sufficient when its content remains readable; attach a recorder for TestServer and other
pipelines that may consume or replace the request content. Use
`recorder.ShouldMatchHttpExchangesSnapshot()` when one snapshot should contain every call made by
the client.

### Multiple snapshots and parameterized tests

Use a variant when one test method produces multiple snapshots or when each parameterized case
needs its own file:

```csharp
var settings = new SnapshotSettings()
    .ForVariant($"status-{statusCode}");

await SnapshotAssert.MatchAsync(result, settings);
```

The variant is appended to the test-derived snapshot name. Snapshot names and variants are encoded
portably, automatically shortened with a stable hash when necessary, and produce the same safe
filename on Windows and Linux. When parameter text should never appear in the filename, use
`ForHashedVariant(parameters)`.

### Targeted JSON transformations

Use extended JSON Pointer rules when a member name should only be transformed at a specific path:

```csharp
var settings = new SnapshotSettings()
    .ScrubPath("/orders/*/id")
    .IgnorePath("/orders/*/generatedAt")
    .ReplacePath("/environment", "test")
    .HashPath("/largePayload")
    .SortArray("/orders", "/id")
    .CanonicalizeJson();

await SnapshotAssert.MatchJsonAsync(json, settings);
```

Paths are case-sensitive. An empty path selects the root, `/` separates segments, and `*` selects
every member or array item at one level. Escape `~` as `~0`, `/` as `~1`, and a literal `*` member
as `~2`. Missing paths are ignored. Array sort keys are compared by their canonical JSON text;
array order remains unchanged unless `SortArray` is configured.

`HashPath` writes a stable `sha256:...` marker based on canonical JSON. It is useful for reducing
large values while still detecting changes, but it is not a substitute for removing secrets with
`IgnorePath`.

`CanonicalizeJson` sorts object properties recursively while preserving array order. `ScrubDateTimes`
only matches ISO-8601 round-trip timestamps. Custom string scrubbers must return valid JSON.

### Diagnostics and safe maintenance

Mismatches identify the first structural difference using JSONPath and expose its values on
`SnapshotMismatchException`:

```csharp
var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(
    () => SnapshotAssert.MatchAsync(result));

Assert.Equal("$.orders[0].status", exception.DifferencePath);
Console.WriteLine($"{exception.ExpectedValue} -> {exception.ActualValue}");
```

Configure project-wide defaults once before test discovery by adding a module initializer to the
test project:

```csharp
using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Snapshots;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettingsDefaults.Global = new(settings => settings
            .BesideSourceFile()
            .ScrubGuids()
            .ScrubDateTimes()
            .ScrubMembers("RequestId", "CorrelationId")
            .IgnoreMembers("AccessToken")
            .CanonicalizeJson()
            .WithoutDiffTool());

        ControllerSnapshotOptionsDefaults.Global = new(options => options
            .IgnoringHeaders("ETag", "X-Request-Nonce")
            .RedactingHeaders("Authorization", "X-Session-Token"));
    }
}
```

Assertions use the global template directly when `snapshotSettings` is omitted:

```csharp
await SnapshotAssert.MatchAsync(result);

using var response = await client.GetAsync("/api/products");
await response.ShouldMatchHttpExchangeSnapshot();
```

Each assertion receives an independent settings copy. Explicit settings are merged into the global
template: scrubbers, member rules, and path rules are combined, while locally configured scalar
values such as the name or directory take precedence. Therefore this keeps global scrubbers and
adds the local one:

```csharp
await response.ShouldMatchHttpExchangeSnapshot(
    snapshotSettings: new SnapshotSettings().ScrubMember("timestamp"));
```

`ExtendGlobal` also accepts an action when a configured settings object is useful before calling
the assertion:

```csharp
var settings = SnapshotSettingsDefaults.ExtendGlobal(settings => settings
    .Named("products")
    .ForVariant($"case-{caseId}"));

await response.ShouldMatchHttpExchangeSnapshot(snapshotSettings: settings);
```

Controller options also merge with their global template. Header and query-parameter decisions are
combined, and an explicit local decision wins for the same name:

```csharp
await response.ShouldMatchControllerSnapshot(
    controllerOptions: new ControllerSnapshotOptions()
        .IgnoringHeaders("Location")
        .IncludingHeader("ETag"));
```

Configure each `Global` once before tests start. Assign `null` to restore package defaults. Complete
HTTP-exchange header filtering remains configured through `HttpExchangeSnapshotOptions` or
`HttpExchangeRecorder`.

Track the snapshots exercised by a complete test scope to find obsolete verified files. A catalog
can remain explicit and instance-scoped so parallel projects do not share its observed-file state:

```csharp
var catalog = new SnapshotCatalog();
var defaults = new SnapshotSettingsDefaults(settings => settings
    .ScrubGuids()
    .TrackingWith(catalog));

await SnapshotAssert.MatchAsync(result, defaults.Create());

// Run only after every snapshot in this catalog's scope has executed.
var obsolete = catalog.FindObsoleteSnapshots(snapshotDirectory);
```

Maintenance is preview-first and requires explicit confirmation:

```csharp
var received = SnapshotMaintenance.FindReceivedSnapshots(snapshotDirectory);
var accepted = SnapshotMaintenance.AcceptReceivedSnapshots(
    snapshotDirectory,
    confirmed: true);
var removed = SnapshotMaintenance.RemoveVerifiedSnapshots(
    obsolete,
    confirmed: true);
```

Review `received` and `obsolete` before changing files. Removal validates every supplied path as a
verified snapshot file before deleting any of them.

Automatic update modes remain disabled in CI unless separately authorized. Set
`INTEGRATION_TESTS_ALLOW_SNAPSHOT_UPDATES_IN_CI=true` or call
`AllowingUpdatesInContinuousIntegration()` in addition to selecting `missing` or `all` update
mode. Keep this opt-in limited to dedicated snapshot-update jobs.

The first run writes a received snapshot. Review and approve it as the verified snapshot; subsequent runs report structural differences. Update modes and local diff viewers are opt-in.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for controller snapshots, scrubbers, and approval workflows.
