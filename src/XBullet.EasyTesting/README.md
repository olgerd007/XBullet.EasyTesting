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

The fluent host also supports configuration overrides, authentication profiles, per-test scenario isolation, and arrange-and-request workflows. Scenario results provide test-framework-agnostic status, header, success, and generic structural JSON body assertions through `result.Should()`.

For an existing `IntegrationTestStartup` pipeline, derive from
`StartupAuthenticatedWebApplicationFactory<IntegrationTestStartup>`. It creates a `TestServer`
directly without invoking `Program.Main`, while retaining configuration and scenario overrides.
Map a Federation scheme with `MapFederation`, then use `AsFederatedUser` to create a primary
`Federation` identity plus an additional `ApiUserIdentity`-shaped identity. Applications that need
a concrete custom identity subclass can replace `ITestClaimsPrincipalFactory` with a derived
`TestClaimsPrincipalFactory`.

External dependencies that need asynchronous startup can implement
`ITestScenarioEnvironmentResource`. Register a new resource for each scenario with
`ConfigureEnvironment`, or register an existing scenario-owned instance through
`TestScenarioScopeBuilder.UseEnvironmentResource`. Resources start before the application host,
can add configuration and services after their endpoint is known, participate in failure
diagnostics, and are disposed after the scenario host.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for complete examples.
