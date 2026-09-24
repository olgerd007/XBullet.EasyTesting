# XBullet.EasyTesting.Verify.Xunit

Optional Verify.Xunit v3 integration for snapshot-testing HTTP controller responses produced by `XBullet.EasyTesting`.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Verify.Xunit
```

```csharp
[Fact]
public async Task Get_order_matches_snapshot()
{
    using var response = await client.GetAsync("/api/orders/42");
    await response.VerifyControllerSnapshot();
}
```

The verified model contains stable request, status, header, and body data. Volatile headers are excluded by default, and `ControllerSnapshotOptions` can customize the captured result.

For a complete request/response exchange, attach an `HttpExchangeRecorder` to the test client.
This records the real TestServer call; it does not replace the transport with a stub:

```csharp
[Fact]
public async Task Create_product_matches_complete_exchange()
{
    var options = new HttpExchangeSnapshotOptions();
    options.Response.IgnoringHeaders("Location");

    var recorder = new HttpExchangeRecorder(options);
    using var client = scope.Client()
        .AsUser(user => user.WithName("snapshot tester"))
        .WithHandler(recorder)
        .Build();

    using var response = await client.PostAsJsonAsync(
        "/api/products",
        new { name = "Webcam", price = 79.95m });

    var settings = new VerifySettings();
    settings.ScrubMember("id");
    await response.VerifyHttpExchangeSnapshot(settings: settings);
}
```

The recorder associates the pre-transport capture with the returned response, so a factory or
client helper can attach a fresh recorder while tests use only the response extension. Without a
recorder, the extension falls back to the request retained by `HttpResponseMessage`. Use
`recorder.VerifyHttpExchangesSnapshot(settings)` when the verified file should contain an array of
every exchange in request order.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for settings and customization examples.
