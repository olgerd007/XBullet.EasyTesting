# XBullet.EasyTesting.Verify.Xunit

Optional Verify.Xunit v3 integration for snapshot-testing HTTP controller responses produced by `XBullet.EasyTesting`.

The package targets .NET 8 and .NET 10.

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

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for settings and customization examples.
