# Verify.Xunit controller snapshots

This guide covers the optional Verify.Xunit v3 adapter for controller responses and complete TestServer exchanges.

## Verify.Xunit v3 controller snapshots

Reference the optional `XBullet.EasyTesting.Verify.Xunit` package from an xUnit v3 test project and verify an HTTP response directly:

```csharp
[Fact]
public async Task Get_order_matches_snapshot()
{
    using var client = _factory.CreateAuthenticatedClient(
        TestUser.Create(name: "Ada", roles: ["Administrator"]));
    using var response = await client.GetAsync("/api/orders/42");

    await response.VerifyControllerSnapshot();
}
```

The Verify snapshot uses the same normalized controller model as the built-in assertion: request method and relative URL, numeric status code, reason phrase, stable response headers, and body. JSON is compared structurally. Empty bodies become `null`, text remains text, and binary content is stored as base64.

Volatile headers such as `Date`, `Server`, and correlation identifiers are excluded by default. Customize the captured data when needed:

```csharp
var options = new ControllerSnapshotOptions()
    .WithoutRequest()
    .IgnoringHeaders("ETag")
    .RedactingHeader("X-Session-Token");

var settings = new VerifySettings();
settings.ScrubMember("createdAt");

await response.VerifyControllerSnapshot(options, settings);
```

For the full request body plus response, attach an `HttpExchangeRecorder` with
`scope.Client().WithHandler(recorder)` as shown in the built-in snapshot section, then verify every
captured exchange in request order:

```csharp
var settings = new VerifySettings();
settings.ScrubMember("id");

await response.VerifyHttpExchangeSnapshot(settings: settings);
```

The recorder continues through the real TestServer pipeline; it is not a stub. Use
`recorder.VerifyHttpExchangesSnapshot(settings)` instead when one Verify file should contain all
captured calls in request order.

Commit each accepted `*.verified.txt` file. Unaccepted `*.received.*` files are ignored by this repository.
