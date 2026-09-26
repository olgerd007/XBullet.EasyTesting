# Built-in snapshot testing

This guide covers the built-in snapshot engine, snapshot formats, transformations, defaults, diagnostics, and approval workflows. Use the package-selection notes to distinguish snapshot core from outbound HTTP adapters.

## Built-in snapshots

Reference `XBullet.EasyTesting.Snapshots.Core` to use snapshot assertions without a dependency on
xUnit, Verify, ASP.NET testing, or the outbound HTTP-stub package. Existing projects can keep
`XBullet.EasyTesting.Snapshots`; it is a binary-compatible facade over core and the HTTP adapter:

```csharp
[Fact]
public async Task Get_order_matches_snapshot()
{
    using var client = _factory.Client()
        .AsUser(user => user.WithName("Ada").WithRole("Administrator"))
        .Build();
    using var response = await client.GetAsync("/api/orders/42");

    var options = new ControllerSnapshotOptions()
        .IgnoringHeaders("ETag");
    var settings = new SnapshotSettings()
        .Named("administrator-order")
        .ScrubMembers("Id", "CreatedAt")
        .IgnoreMembers("AccessToken")
        .ScrubGuids()
        .ScrubDateTimes();

    await response.ShouldMatchControllerSnapshot(options, settings);
}
```

The first run writes a runtime-qualified file such as
`__snapshots__/TestFile.TestMethod.received.net8.0.json` and fails with a
`SnapshotMismatchException`. Accept `exception.ReceivedPath` to promote it to the shared
`.verified.json` file. Runtime qualification prevents parallel target frameworks from overwriting
each other's received output.

The lower-level assertion works with any serializable value:

```csharp
await SnapshotAssert.MatchAsync(result);
```

Use `InDirectory(path)` to choose another snapshot directory. Relative paths are resolved from the
calling source file. To store snapshots directly beside that source file, use
`new SnapshotSettings().BesideSourceFile()`. The `InDirectory(context => ...)` overload supports
centralized layouts based on the source file, test name, snapshot name, and variant.

Use `SnapshotAssert.MatchTextAsync(text)` for plain text. It produces `.verified.txt` and
runtime-qualified `.received.*.txt` files without applying JSON parsing.

Raw JSON content has a dedicated assertion so it is parsed and normalized rather than captured as
an escaped JSON string. Serialization uses `System.Text.Json`; verified files use `.verified.json`
and received files include the target framework:

```csharp
var settings = new SnapshotSettings()
    .ScrubGuids();

await SnapshotAssert.MatchJsonAsync(json, settings);

using var response = await client.GetAsync("/api/orders/42");
response.EnsureSuccessStatusCode();
await response.ShouldMatchJsonBodySnapshot();
```

Use `MatchJsonAsync` for a raw JSON string and `ShouldMatchJsonBodySnapshot` when only an HTTP
response body belongs in the snapshot. `response.Content.ShouldMatchJsonSnapshot()` provides the
same behavior when only the content is in scope. Buffered or seekable content is rewound for the
assertion and its original position is restored. Use `ShouldMatchControllerSnapshot` when request
metadata, status, and stable headers should be included too.

To snapshot the complete request and response from a real controller call, add an
`HttpExchangeRecorder` to the TestServer client. This is a delegating handler, not an HTTP stub:

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

The recorder captures the request before TestServer can consume its body, captures response
metadata before returning it, and records response content as the caller reads it. This preserves
`ResponseHeadersRead` and streaming behavior. An unread response body appears as `{NotRead}`, while
a content-read error is recorded as `BodyFailure` without being misclassified as a send failure.
The recorder associates that capture with the response, so a factory or client helper can install
a fresh recorder and tests only need the response extension. JSON bodies are structural, and
request and response header filtering/redaction are configured independently. Authentication,
XBullet test-transport, cookie, API-key, correlation, and tracing headers are excluded by default.

Without a recorder, `response.ShouldMatchHttpExchangeSnapshot(options)` falls back to
`response.RequestMessage`. Attach a recorder for TestServer and other pipelines that may consume or
replace request content. Use `recorder.ShouldMatchHttpExchangesSnapshot()` to snapshot every call
made by one client as an ordered array.

Complete exchange snapshots use JSON by default. Select an HTTP-style transcript stored as
`.verified.txt`, or deterministic YAML stored as `.verified.yaml`, on the exchange options. All
formats use the same structural JSON scrubbing before rendering:

```csharp
var exchangeOptions = new HttpExchangeSnapshotOptions
{
    Format = HttpExchangeSnapshotFormat.Http // or Yaml
};

await response.ShouldMatchHttpExchangeSnapshot(
    exchangeOptions,
    new SnapshotSettings().ScrubMember("id"));
```

Controller snapshots exclude volatile and sensitive headers by default, including `Date`, tracing
identifiers, `Set-Cookie`, `Authentication-Info`, and `Proxy-Authentication-Info`. Preserve a
header's presence without exposing its value, or omit all headers:

```csharp
var redacted = new ControllerSnapshotOptions()
    .RedactingHeaders("Set-Cookie", "X-Session-Token")
    .RedactingQueryParameter("tenant_secret")
    .ScrubbingUrlPathGuids()
    .ScrubbingQueryParameters("timestamp", "requestId");

var bodyOnly = new ControllerSnapshotOptions()
    .WithoutRequest()
    .WithoutHeaders();
```

Redacted values appear as `{Redacted}`. Common secret-bearing query parameters such as
`access_token`, `api_key`, `client_secret`, `sas`, `secret`, `sig`, and `token` are redacted by default.
`IncludingHeader(name)` or `IncludingQueryParameter(name)` explicitly restores a value when it is
safe and stable.

URL scrubbing preserves stable route structure while replacing volatile values. Complete GUID path
segments become `{Guid}`, and configured query values become `{Scrubbed}`. For other route values,
use `ScrubbingUrlPath(path => ...)`; the callback receives only the relative URL path, without its
query or fragment. These methods are also available on `HttpExchangeRequestSnapshotOptions` and
`StubHttpRequestSnapshotOptions`. If a query name is both redacted and scrubbed, security redaction
wins.

For multiple snapshots or parameterized cases in one test method, append a stable variant:

```csharp
await SnapshotAssert.MatchAsync(
    result,
    new SnapshotSettings().ForVariant($"case-{caseId}"));
```

Long names are shortened with a deterministic hash. Use `ForHashedVariant(parameters)` when raw
parameter text should never be included in the filename.

The remaining work is tracked in the [snapshot package roadmap](../snapshots-roadmap.md).

Target individual values with extended JSON Pointer rules when global member-name scrubbing would
hide too much:

```csharp
var settings = new SnapshotSettings()
    .ScrubPath("/orders/*/id")
    .IgnorePath("/orders/*/generatedAt")
    .HashPath("/largePayload")
    .SortArray("/orders", "/id")
    .CanonicalizeJson();
```

The `*` segment selects one object or array level. Exact path names are case-sensitive.

Structured scrubbers are applied recursively to objects and arrays. `ScrubMembers` preserves a member but stores `{Scrubbed}` instead of its dynamic value; `IgnoreMembers` removes it. Member matching is case-insensitive. `ScrubGuids` and `ScrubDateTimes` replace matching JSON string values with `{Guid}` and `{DateTime}`. For specialized transformations, the existing `Scrub(content => ...)` string scrubber remains available. Parameterized tests should set a unique snapshot name for each case.

Snapshot mismatch messages report the first structural difference as JSONPath with compact
expected and actual values. The same details are available through `DifferencePath`,
`ExpectedValue`, and `ActualValue` on `SnapshotMismatchException`.

### Project-wide snapshot defaults

Set `SnapshotSettingsDefaults.Global`, `ControllerSnapshotOptionsDefaults.Global`, and
`HttpExchangeSnapshotOptionsDefaults.Global` once during test-assembly initialization to apply
shared snapshot and HTTP-capture conventions. A module initializer runs before the test framework
discovers or executes tests, so it is a convenient place for this setup. Add a file such as
`SnapshotConfiguration.cs` to the test project:

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

        HttpExchangeSnapshotOptionsDefaults.Global = new(options =>
        {
            options.Format = HttpExchangeSnapshotFormat.Yaml;
            options.Request.IgnoringHeaders("X-Request-Nonce");
            options.Response.IgnoringHeaders("ETag");
        });
    }
}
```

Assertions use those defaults directly when settings are omitted, including complete HTTP
exchange assertions:

```csharp
await SnapshotAssert.MatchAsync(result);

using var response = await client.GetAsync("/api/products");
await response.ShouldMatchHttpExchangeSnapshot();
```

The configured object is a template, not a shared mutable settings instance. Every assertion gets
an independent copy, so scrubbers and local changes are safe when tests run in parallel. Explicit
settings are merged into the global template: scrubbers, member rules, and path rules are combined,
while locally configured scalar values such as the name or directory take precedence. Therefore
this keeps global scrubbers and adds the local one:

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

Explicit controller options merge the same way: header and query-parameter decisions are combined,
and explicit local decisions win when the same name is configured globally and locally:

```csharp
await response.ShouldMatchControllerSnapshot(
    controllerOptions: new ControllerSnapshotOptions()
        .IgnoringHeaders("Location")
        .IncludingHeader("ETag"));
```

Explicit HTTP exchange options are also merged over their global template. Use
`HttpExchangeSnapshotOptionsDefaults.ExtendGlobal(...)` when you need a configured independent
copy before constructing an `HttpExchangeRecorder` or calling an assertion.

Configure each `Global` once before tests start. Assign `null` to restore package defaults, which is
primarily useful for test-host isolation.

### Reusable snapshot defaults and obsolete-file detection

Use an instance-scoped defaults template when conventions should be shared only by a particular
test suite or fixture. Each call to `Create` returns an independent settings instance:

```csharp
private static readonly SnapshotCatalog SnapshotCatalog = new();

private static readonly SnapshotSettingsDefaults SnapshotDefaults = new(settings => settings
    .ScrubGuids()
    .ScrubDateTimes()
    .TrackingWith(SnapshotCatalog)
    .WithoutDiffTool());

var settings = SnapshotDefaults.Create(settings => settings.ForVariant($"{caseId}"));
await SnapshotAssert.MatchAsync(result, settings);
```

After every snapshot in the catalog's intended scope has run, detect verified files that were not
observed:

```csharp
var obsolete = SnapshotCatalog.FindObsoleteSnapshots(snapshotDirectory);
```

Do not perform this audit after a filtered or failed test run: unexecuted snapshots would appear
obsolete.

### Diff viewer and snapshot acceptance

On a local non-CI run, a mismatch automatically opens a detected Visual Studio, VS Code, Rider, or Meld diff viewer. Disable this behavior or select a tool explicitly:

```csharp
var settings = new SnapshotSettings()
    .WithoutDiffTool();

var vscodeSettings = new SnapshotSettings()
    .WithDiffTool(SnapshotDiffTool.VisualStudioCode());
```

Promote a received file explicitly:

```csharp
SnapshotAssert.AcceptReceived(exception.ReceivedPath);
```

Automatic update modes are opt-in:

```csharp
var settings = new SnapshotSettings()
    .Updating(SnapshotUpdateMode.Missing); // or All
```

They can also be selected for a test run with `INTEGRATION_TESTS_UPDATE_SNAPSHOTS=missing` or `INTEGRATION_TESTS_UPDATE_SNAPSHOTS=all`. `missing` creates only absent verified files; `all` also replaces changed verified files. Never enable `all` in a normal CI verification run.

CI requires a second explicit authorization before either automatic update mode can write:

```shell
INTEGRATION_TESTS_UPDATE_SNAPSHOTS=all
INTEGRATION_TESTS_ALLOW_SNAPSHOT_UPDATES_IN_CI=true
```

Code can opt in with `AllowingUpdatesInContinuousIntegration()`. Limit either form to a dedicated
snapshot-update job.

Preview and then explicitly confirm bulk maintenance operations:

```csharp
var received = SnapshotMaintenance.FindReceivedSnapshots(snapshotDirectory);
var accepted = SnapshotMaintenance.AcceptReceivedSnapshots(
    snapshotDirectory,
    confirmed: true);

var obsolete = SnapshotCatalog.FindObsoleteSnapshots(snapshotDirectory);
var removed = SnapshotMaintenance.RemoveVerifiedSnapshots(
    obsolete,
    confirmed: true);
```

Acceptance and removal throw unless `confirmed: true` is supplied. Removal validates the complete
input before deleting any verified file.
