# XBullet.EasyTesting

Reusable infrastructure for integration-testing authenticated ASP.NET Core applications through an in-memory `TestServer`.

The package targets .NET 8, .NET 9, and .NET 10.

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

To keep the application's real default authentication scheme, configure
`PreserveDefaultAuthenticationScheme().MapTestAuthentication("IntegrationTest")` and select the
named scheme with `AsUser(..., authenticationScheme: "IntegrationTest")`. This supports a hybrid
test project: use simulated principals for authorization and business behavior, and real ASP.NET
Core Identity endpoints and handlers for registration, login, passwords, and token lifecycle.
To let both identity types use the same plain `RequireAuthorization()` endpoint, configure
`UseHybridDefaultAuthentication("IntegrationTest")`; requests carrying XBullet's test-identity
header use the simulated scheme, other requests use the application's original default scheme,
and the original challenge and forbid handlers remain active. This behavior is opt-in.
`SeedIdentityUserAsync` persists users through `UserManager<TUser>`,
`CreateIdentityTestUserAsync` creates linked simulated users, and `WithBearerToken` sends real
Identity bearer tokens without JWT-specific test configuration. Identity stores without role or
claim support produce linked simulated users with empty role or claim collections.

Use `EasyTestHost.Create<Program>().UseSetting(key, value)` for connection strings and other values
read immediately after `WebApplication.CreateBuilder`; these early settings are visible before
minimal-hosting startup code consumes them.

External dependencies that need asynchronous startup can implement
`ITestScenarioEnvironmentResource`. Register a new resource for each scenario with
`ConfigureEnvironment`, or register an existing scenario-owned instance through
`TestScenarioScopeBuilder.UseEnvironmentResource`. Resources start before the application host,
can add configuration and services after their endpoint is known, participate in failure
diagnostics, and are disposed after the scenario host.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for complete examples.
