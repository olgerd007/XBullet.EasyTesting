# XBullet.EasyTesting

Reusable infrastructure for integration-testing authenticated ASP.NET Core applications through an
in-memory `TestServer`.

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

The fluent host also supports configuration overrides, authentication profiles, per-test scenario
isolation, and arrange-and-request workflows. Scenario results provide test-framework-agnostic
status, header, success, and generic structural JSON body assertions through `result.Should()`.

The host environment defaults to `Testing`. Chain `.UseEnvironment("Development")` before
`.Build()` to test a different environment. The selection applies during application startup and
is inherited by scenario hosts.

Derive domain helpers from `Scenario<TEntryPoint>` to share arrangement guards and scope access.
Use `Arrange()` to prepare one fluent request, or `ArrangeAsync()` to apply setup before multiple
operations in the same scope. `ScopedTest<TEntryPoint, TFactory>` provides a borrowed `Factory` and
protected `RunAsync` helpers with automatic isolation, failure diagnostics, and cleanup, without a
test-framework dependency. Pass the framework's test cancellation token to its constructor and
keep fixture registration and factory ownership in the test project.

Import `XBullet.EasyTesting` and use `Eventually.AssertAsync` to retry assertions against background
work, or `Eventually.WaitUntilAsync` to poll a synchronous or asynchronous condition. Both support
caller cancellation and configurable cooperative deadlines, sequential polling, and `TimeProvider`.
Timeouts retain the last assertion failure and report elapsed time and attempt count. Callbacks
must complete promptly or observe the supplied cancellation token.

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

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting.html)
- [Authentication and scenarios guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/authentication-and-scenarios.md)
- [Test hosts, lifecycle, and authentication concepts](https://github.com/olgerd007/XBullet.EasyTesting/tree/main/docs/concepts)
- [Response assertions](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/response-assertions.md)
- [Wait for background work](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/eventual-assertions.md)
- [Documentation home and package selection](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/index.md)
- [Executable integration-test examples](https://github.com/olgerd007/XBullet.EasyTesting/tree/main/tests/TestApi.IntegrationTests)
