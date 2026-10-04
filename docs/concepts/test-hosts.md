# Choose a test host

XBullet.EasyTesting offers several host entry points because applications do not all start or
compose in the same way. Choose the host based on application startup and test customization, not
on the endpoint being tested.

## Host selection

| Host | Choose it when | Configuration style |
| --- | --- | --- |
| `AuthenticatedWebApplicationFactory<TEntryPoint>` | The application uses modern `Program`-based hosting and a reusable subclass makes test setup clearer | Override protected configuration hooks or use static factory helpers |
| `EasyTestHost.Create<TEntryPoint>()` | The application uses `Program`-based hosting and test modules should compose without a custom subclass | Chain configuration, service, authentication, environment-resource, and extension-package callbacks |
| `StartupAuthenticatedWebApplicationFactory<TStartup>` | A test-only or legacy application exposes `Startup` and must not execute `Program.Main` | Override Startup-specific configuration and authentication hooks |
| `EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` | A `Program`-based application also needs database actions and lifecycle management | Derive a reusable factory and configure the test provider |
| `StartupEntityFrameworkWebApplicationFactory<TStartup, TDbContext>` | A `Startup` application also needs database actions and lifecycle management | Combine Startup hosting with EF Core hooks |

All hosts use ASP.NET Core `TestServer`. Use `XBullet.EasyTesting.Aspire` instead when a test must
start the complete distributed topology, real processes, or AppHost-managed resources.

## Use a reusable factory

A reusable factory is a good fit when many tests share the same service replacements,
authentication-scheme mappings, database provider, or scenario resources. Keep the factory at
fixture scope and put mutable test state inside a scenario scope.

```csharp
public sealed class OrdersApiFactory : AuthenticatedWebApplicationFactory<Program>
{
    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication) =>
        authentication.MapAzureAd("Bearer");

    protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    }
}
```

The factory owns the root test host. The xUnit fixture disposes it after the test class. Individual
tests should create and dispose clients, responses, results, and scenario scopes.

## Compose a host without a subclass

`EasyTestHost` applies repeated callbacks in registration order. This lets package modules and
test-specific overrides participate independently:

<!-- snippet: tests/TestApi.IntegrationTests/ComposableHostTests.cs#docs-composable-host -->
```csharp
[Fact]
public async Task Builder_composes_multiple_authentication_modules()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var builder = TestApiHostSettings.CreateBuilder()
        .ConfigureConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["EasyTesting:Source"] = "composable-builder"
            }))
        .ConfigureServices(services => services.AddSingleton(
            new HostMarker("registered")))
        .ConfigureAuthentication(authentication => authentication.MapAzureAd("AzureAd"))
        .ConfigureAuthentication(authentication => authentication.MapApiKey("ApiKey"));
    using var factory = builder.Build();
    var arranged = false;
    using var azureAdScenario = await factory.Scenario()
        .Arrange(_ =>
        {
            arranged = true;
            return Task.CompletedTask;
        })
        .AsAzureAdUser(user => user
            .WithTenantId("tenant-42")
            .WithScope("orders.read"))
        .Get("/api/secure/azure-ad")
        .ExecuteAsync(cancellationToken);
    using var apiKeyClient = factory.Client()
        .AsApiKey(apiKey => apiKey.WithKeyId("partner-key"))
        .Build();

    using var apiKeyResponse = await apiKeyClient.GetAsync(
        "/api/secure/api-key",
        cancellationToken);

    Assert.True(arranged);
    Assert.Equal(HttpStatusCode.NoContent, azureAdScenario.Response.StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, apiKeyResponse.StatusCode);
    Assert.Equal(
        "composable-builder",
        factory.Services.GetRequiredService<IConfiguration>()["EasyTesting:Source"]);
    Assert.Equal(
        "registered",
        factory.Services.GetRequiredService<HostMarker>().Value);
    Assert.Throws<InvalidOperationException>(() => builder.Build());
}
```
<!-- end-snippet -->

An `EasyTestHostBuilder` builds once. Add every module before calling `Build`; attempting to build
the same builder again throws `InvalidOperationException`.

Use `UseSetting` for values read during minimal-host startup, such as connection strings consumed
immediately after `WebApplication.CreateBuilder`. Later `ConfigureConfiguration` callbacks cannot
change values that the application has already consumed.

## Choose the host environment

The factory defaults to `Testing`. Use `UseEnvironment` to exercise environment-dependent startup
and middleware, including `Development`, `Production`, or a custom environment name:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .UseEnvironment("Development")
    .Build();
```

The selected environment is available when `WebApplication.CreateBuilder` returns, so startup
checks such as `builder.Environment.IsDevelopment()` and environment-specific configuration use
that name. Scenario child hosts inherit it. Repeated calls follow host-setting registration order;
the last environment setting wins. Null, empty, and whitespace names are rejected.

`ConfigureEnvironment` configures external scenario resources; use `UseEnvironment` to select
the ASP.NET Core host environment.

## Host a Startup application

The Startup hosts create `TestServer` directly and do not invoke an application entry point. The
following executable example supplies test configuration and a federated identity to a Startup
pipeline:

<!-- snippet: tests/TestApi.IntegrationTests/StartupHostTests.cs#docs-startup-host -->
```csharp
[Fact]
public async Task Startup_authenticated_host_runs_without_an_entry_point()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    using var factory = new AuthenticationOnlyFederationTestHost();

    using var result = await factory.Scenario()
        .AsFederatedUser(user => user.WithApiUserClaim("portfolio", "portfolio-9"))
        .Get("/federation/context")
        .ExecuteAsync(cancellationToken);
    var body = await result.Response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

    Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
    Assert.Equal("injected-appsettings", body.GetProperty("configurationSource").GetString());
    Assert.Equal(0, body.GetProperty("productCount").GetInt32());
    Assert.Equal(
        TestAuthenticationDefaults.FederationAuthenticationType,
        body.GetProperty("authenticationTypes")[0].GetString());
    Assert.Equal(
        TestAuthenticationDefaults.ApiUserIdentityAuthenticationType,
        body.GetProperty("authenticationTypes")[1].GetString());
}
```
<!-- end-snippet -->

Choose this path only when the desired pipeline is fully described by `TStartup`. If production
behavior is implemented in `Program` before Startup registration, a Startup host intentionally does
not execute that behavior.

## Factory and scenario lifetimes

The factory can be shared, but a scenario scope represents one isolated test lifetime:

1. Start environment resources.
2. Apply their configuration and services.
3. Build the scenario child host.
4. Initialize scenario resources and the database.
5. Execute requests and assertions.
6. Capture diagnostics if the test fails.
7. Clean up resources and the database.
8. Dispose the child host and environment resources.

See [scenarios and isolation](scenarios-and-isolation.md) for concurrency and cleanup behavior.

## Related documentation

- [Run your first controller test](../getting-started/first-controller-test.md)
- [Authentication models](authentication.md)
- [Override services and configuration](../guides/service-and-configuration-overrides.md)
- [Test with Entity Framework Core](../guides/entity-framework-core.md)
