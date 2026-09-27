# Understand resources and cleanup

XBullet.EasyTesting distinguishes resources that already exist inside a host from resources that
must start before the host can be configured. Both participate in scenario diagnostics and cleanup.

## Resource types

| Contract | Use it for | Lifecycle |
| --- | --- | --- |
| `ITestScenarioResource` | In-memory stubs, recorders, collectors, or mutable helpers registered with a factory | Reset before and after a scenario; capture diagnostics on failure |
| `ITestScenarioEnvironmentResource` | Containers, emulators, servers, or other dependencies whose endpoint is needed to configure the host | Start before host creation, contribute configuration and services, capture diagnostics, then dispose after the host |

`StubHttpMessageHandler`, `RecordedMessageBus`, and observability collectors implement scenario
resource behavior. Testcontainers use the environment-resource lifecycle.

## Register reusable scenario resources

Register a stable resource instance once in a factory:

```csharp
public StubHttpMessageHandler ExternalCatalog { get; } = new();

public OrdersApiFactory()
{
    RegisterScenarioResource("External catalog", ExternalCatalog);
}
```

Rules and recorded calls are reset around each scenario scope. The registration name appears in
failure diagnostics, so use a short name that identifies the application boundary.

## Start resources before the host

Configure an environment-resource factory when each scenario needs a fresh dependency. The
resource starts first, then contributes endpoint configuration and service registrations before the
child host is built:

<!-- snippet: tests/TestApi.IntegrationTests/ScenarioEnvironmentResourceTests.cs#docs-environment-resource-lifecycle -->
```csharp
[Fact]
public async Task Environment_resource_starts_before_host_configuration_and_is_disposed_after_host()
{
    var events = new List<string>();
    RecordingEnvironmentResource? resource = null;
    using var factory = TestApiHostSettings.CreateBuilder()
        .ConfigureEnvironment(environment => environment.AddResource(
            "dependency",
            context => resource = new RecordingEnvironmentResource(
                context.ScenarioId,
                events)))
        .Build();

    await using (var scope = await factory.CreateTestScenarioScopeAsync(
        cancellationToken: TestContext.Current.CancellationToken))
    {
        Assert.NotNull(resource);
        Assert.Same(resource, scope.GetEnvironmentResource<RecordingEnvironmentResource>(
            "dependency"));
        Assert.Equal(scope.ScenarioId, resource.ScenarioId);
        Assert.Equal(
            resource.Endpoint,
            scope.Services.GetRequiredService<IConfiguration>()["Dependency:Endpoint"]);
        Assert.Same(resource, scope.Services.GetRequiredService<RecordingEnvironmentResource>());
        Assert.Equal("started", events[0]);
        Assert.Contains("configuration", events);
        Assert.Contains("services", events);
        Assert.DoesNotContain("disposed", events);
    }

    Assert.Equal("disposed", events[^1]);
}
```
<!-- end-snippet -->

The lifecycle order is:

1. Construct the resource with the scenario context.
2. Call `StartAsync`.
3. Call `ConfigureConfiguration`.
4. Call `ConfigureServices`.
5. Build and run the application host.
6. Call `CaptureDiagnosticsAsync` if the scenario fails.
7. Dispose the application host.
8. Call `DisposeAsync` on environment resources in reverse creation order.

Register an already-created, scenario-owned instance with
`TestScenarioScopeBuilder.UseEnvironmentResource`. Do not register a shared external resource this
way unless disposing it after the scenario is intended.

## Ownership helpers

`TestScenarioContext.DisposeWithScenario` attaches an `IDisposable` or `IAsyncDisposable` object to
the scenario host. Use it for objects created while configuring per-scenario services, such as an
open database connection. The context disposes attached objects when the scenario host is torn down.

The scope, result, client, response, and external resource have separate ownership:

- The fixture owns and disposes the root factory.
- The test asynchronously disposes the scenario scope.
- The test disposes each client, response, or scenario result it creates.
- The scope owns resources registered through its context or environment-resource builder.
- A caller-provided dependency remains caller-owned unless an API explicitly transfers ownership.

## Startup and cleanup failures

If one environment resource fails during startup, the scope disposes that resource and every
resource that started before it. The scenario gate is released so a later scope can start.

If a scenario fails, diagnostics are captured before cleanup. If cleanup also fails, inspect the
original exception's diagnostic data and the terminal cleanup exception according to the package's
documented behavior. Database and SQLite cleanup provide additional diagnostics described in the
[EF Core guide](../guides/entity-framework-core.md#cleanup-and-diagnostics).

## Related documentation

- [Scenarios and isolation](scenarios-and-isolation.md)
- [Choose a test host](test-hosts.md)
- [Testcontainers package guide](../../src/XBullet.EasyTesting.Testcontainers/README.md)
