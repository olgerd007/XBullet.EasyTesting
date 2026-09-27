# Override services and configuration

Use test-host overrides to replace external boundaries, clocks, configuration values, and other
dependencies without changing production startup. Choose factory-wide or scenario-specific scope
based on whether the replacement contains mutable test state.

## Choose override scope

| Scope | Use it for | Avoid it for |
| --- | --- | --- |
| Factory-wide | Stable adapters, immutable fakes, authentication mappings, common configuration | Per-test expectations, recorded calls, or values mutated by parallel tests |
| Scenario-specific | One test's configuration, service replacement, database connection, or mutable fake | Expensive dependencies intended to be shared across independent factories |
| Early host setting | Values consumed during `WebApplication.CreateBuilder` or immediately afterward | Values that can be changed normally through scenario configuration |

## Compose factory-wide overrides

With `EasyTestHost`, register callbacks before `Build`:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .UseSetting("ConnectionStrings:Orders", connectionString)
    .ConfigureConfiguration(configuration =>
        configuration.AddInMemoryCollection(testSettings))
    .ConfigureServices(services =>
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    })
    .Build();
```

Callbacks of the same kind execute in registration order. Later service registrations follow the
normal Microsoft dependency-injection rules; remove production descriptors before registering a
replacement when resolving a single service must be deterministic.

For a derived factory, override `ConfigureTestConfiguration`,
`ConfigureAdditionalServicesForTests`, and `ConfigureTestAuthentication` instead. Keep production
registration in the application and test-only replacement in the test project.

## Apply per-scenario overrides

Pass a scope callback when creating the scenario. Configuration and service callbacks apply only to
the child host. This executable test proves that replacements and values disappear in the next
scope:

<!-- snippet: tests/TestApi.IntegrationTests/TestScenarioScopeTests.cs#docs-scenario-isolation -->
```csharp
[Fact]
public async Task Scope_isolates_database_resources_services_and_configuration()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var replacement = new ReplacementCatalogClient();

    await using (var first = await _factory.CreateTestScenarioScopeAsync(
        scope => scope
            .ConfigureConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Scenario:Name"] = "first"
                }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IExternalCatalogClient>();
                services.AddSingleton<IExternalCatalogClient>(replacement);
            }),
        cancellationToken))
    {
        Assert.Same(replacement, first.Services.GetRequiredService<IExternalCatalogClient>());
        Assert.Equal("first", first.Services.GetRequiredService<IConfiguration>()["Scenario:Name"]);

        await _factory.WithScenarioDbContextAsync(
            first,
            async (database, token) =>
            {
                database.Products.Add(new Product { Id = 901, Name = "Scoped", Price = 10m });
                await database.SaveChangesAsync(token);
            },
            cancellationToken);
        var productCount = await _factory.WithScenarioDbContextAsync(
            first,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);
        Assert.Equal(1, productCount);

        _factory.ExternalCatalog
            .When(HttpMethod.Get, "/scope")
            .Respond(System.Net.HttpStatusCode.OK);
        using var externalClient = new HttpClient(_factory.ExternalCatalog, disposeHandler: false)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        using var response = await externalClient.GetAsync("/scope", cancellationToken);
        _factory.PublishedMessages.Record("test", "scope", new { Value = 42 });

        Assert.Equal(1, _factory.ExternalCatalog.CallCount);
        Assert.Equal(1, _factory.PublishedMessages.Count);
    }

    Assert.Equal(0, _factory.ExternalCatalog.CallCount);
    Assert.Equal(0, _factory.PublishedMessages.Count);

    await using var second = await _factory.CreateTestScenarioScopeAsync(
        cancellationToken: cancellationToken);
    var secondProductCount = await _factory.WithScenarioDbContextAsync(
        second,
        (database, token) => database.Products.CountAsync(token),
        cancellationToken);

    Assert.Equal(0, secondProductCount);
    Assert.IsType<ExternalCatalogClient>(
        second.Services.GetRequiredService<IExternalCatalogClient>());
    Assert.Null(second.Services.GetRequiredService<IConfiguration>()["Scenario:Name"]);
}
```
<!-- end-snippet -->

When a replacement must be disposed with the child host, create it during scenario configuration
and attach it with `TestScenarioContext.DisposeWithScenario`.

## Override values read during startup

Minimal-host applications can read configuration before ordinary test callbacks run. Use
`UseSetting(key, value)` or `CreateWithHostSettings` for connection strings and similar values read
immediately after `WebApplication.CreateBuilder`.

Use later `ConfigureConfiguration` callbacks for values resolved through `IConfiguration` after
the host has started. If changing a value has no effect, identify when production startup reads and
caches it before moving the override earlier.

## Diagnose an ineffective replacement

Check these common causes:

- The application resolved or copied the value before the override was applied.
- The production descriptor was not removed, and the consumer resolves a different registration.
- The application uses `IHttpClientFactory`, but only a standalone `HttpClient` was replaced.
- A singleton captured the original dependency before scenario-specific service configuration.
- The test changed the root factory instead of the isolated scenario child host.
- Options were bound and cached before the test changed their configuration source.

Resolve the service from `scope.Services` to verify the scenario host sees the intended instance.
Do not resolve scoped services from the root factory provider.

## Related documentation

- [Choose a test host](../concepts/test-hosts.md)
- [Scenarios and isolation](../concepts/scenarios-and-isolation.md)
- [Resources and cleanup](../concepts/resources-and-cleanup.md)
- [Core API reference](../api/packages/xbullet-easytesting.md)
