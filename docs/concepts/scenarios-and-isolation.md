# Understand scenarios and isolation

A factory can be shared across tests, but mutable state should not be. `TestScenarioScope` creates a
child host and defines the lifetime for configuration overrides, service replacements, database
state, recorders, external resources, diagnostics, and cleanup.

## Scenario lifecycle

`CreateTestScenarioScopeAsync` acquires the factory's scenario gate and holds it until the scope is
disposed. The gate prevents two scopes from mutating the same registered resources or shared
database hooks concurrently.

<!-- snippet: tests/TestApi.IntegrationTests/TestScenarioScopeTests.cs#docs-scenario-lifetime-gate -->
```csharp
[Fact]
public async Task Scope_gate_covers_the_complete_test_lifetime()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    await using var first = await _factory.CreateTestScenarioScopeAsync(
        cancellationToken: cancellationToken);

    var secondTask = _factory.CreateTestScenarioScopeAsync(cancellationToken: cancellationToken);
    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);

    Assert.False(secondTask.IsCompleted);

    await first.DisposeAsync();
    await using var second = await secondTask;
    Assert.NotEqual(first.ScenarioId, second.ScenarioId);
}
```
<!-- end-snippet -->

Always use `await using` for a scope. Cancellation during startup or execution still runs cleanup
for resources that were successfully created.

## Fluent request scenarios

`scope.Scenario()` creates a `TestScenarioBuilder<TEntryPoint>` for one arrange-and-request flow:

1. Add zero or more `Arrange` callbacks.
2. Select one authentication profile.
3. Configure request headers or content.
4. Select exactly one request method and URI.
5. Call `ExecuteAsync` once.
6. Assert through the returned `TestScenarioResult`.
7. Dispose the result.

A scenario without a request, with more than one request, or executed more than once throws
`InvalidOperationException`. This keeps ownership and diagnostics unambiguous.

See the
[realistic controller scenario](../getting-started/first-controller-test.md#arrange-data-and-assert-the-response)
for an executable arrange, act, assert, and cleanup example.

## What a scope isolates

Scenario-specific callbacks can replace configuration and services without changing the shared
factory. EF-backed factories can create an isolated database for the same scope. Registered
`ITestScenarioResource` instances reset before and after execution.

The executable isolation example combines configuration, service replacement, database state, an
HTTP recorder, and a message recorder, then proves that the next scope starts clean:

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

Do not store test-specific values in static state or mutate singleton services shared by the root
factory. Replace them within the scenario child host instead.

## Failure diagnostics

`RunInTestScenarioScopeAsync` captures diagnostics before cleanup when the test delegate throws.
The original exception remains the thrown exception, and a `TestScenarioDiagnostics` value is added
to its `Data` dictionary under `TestScenarioDiagnostics.ExceptionDataKey`.

<!-- snippet: tests/TestApi.IntegrationTests/TestScenarioScopeTests.cs#docs-scenario-failure-diagnostics -->
```csharp
[Fact]
public async Task Failed_scope_captures_diagnostics_before_automatic_cleanup()
{
    var cancellationToken = TestContext.Current.CancellationToken;

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _factory.RunInTestScenarioScopeAsync(
            async (_, token) =>
            {
                _factory.ExternalCatalog
                    .When(HttpMethod.Get, "/diagnostic")
                    .Respond(System.Net.HttpStatusCode.OK);
                using var externalClient = new HttpClient(
                    _factory.ExternalCatalog,
                    disposeHandler: false)
                {
                    BaseAddress = new Uri("https://external.example.test/")
                };
                using var response = await externalClient.GetAsync("/diagnostic", token);
                _factory.PublishedMessages.Record(
                    "test",
                    "diagnostic-events",
                    new { Reason = "failure" });

                throw new InvalidOperationException("Expected test failure.");
            },
            cancellationToken: cancellationToken));

    var diagnostics = Assert.IsType<TestScenarioDiagnostics>(
        exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);
    var serialized = JsonSerializer.Serialize(diagnostics);
    Assert.Contains("External HTTP", serialized);
    Assert.Contains("/diagnostic", serialized);
    Assert.Contains("Published messages", serialized);
    Assert.Contains("diagnostic-events", serialized);
    Assert.Contains("ProviderName", serialized);
    Assert.Equal(0, _factory.ExternalCatalog.CallCount);
    Assert.Equal(0, _factory.PublishedMessages.Count);
}
```
<!-- end-snippet -->

Diagnostics can include registered scenario resources, environment resources, database provider
details, and other package contributions. Capture happens before reset and disposal so failure state
is not lost. Cleanup then runs even though the delegate failed.

## Parallel execution

Scopes created from one factory are intentionally serialized by the scenario gate. Use separate
factory instances only when their application state, ports, databases, and external resources are
also independent. Snapshot files and other external artifacts have their own concurrency rules.

## Related documentation

- [Resources and cleanup](resources-and-cleanup.md)
- [Override services and configuration](../guides/service-and-configuration-overrides.md)
- [Test with Entity Framework Core](../guides/entity-framework-core.md)
