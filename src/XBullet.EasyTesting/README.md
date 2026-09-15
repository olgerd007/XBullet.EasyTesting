# XBullet.EasyTesting

Reusable infrastructure for integration-testing authenticated ASP.NET Core applications through an in-memory `TestServer`.

The package targets .NET 8 and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting
```

## Example

```csharp
using var factory = EasyTestHost.Create<Program>()
    .ConfigureServices(services =>
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    })
    .Build();

using var client = factory.Client()
    .AsUser(user => user
        .WithName("Ada")
        .WithRole("Administrator"))
    .Build();

using var response = await client.GetAsync("/api/orders");
response.EnsureSuccessStatusCode();
```

The fluent host also supports configuration overrides, authentication profiles, per-test scenario isolation, and arrange-and-request workflows.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for complete examples.
