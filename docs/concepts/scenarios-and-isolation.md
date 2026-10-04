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

## Reusable domain scenarios

Derive from `Scenario<TEntryPoint>` to give arrangements domain-specific names. The base borrows an
existing `TestScenarioScope<TEntryPoint>` and exposes it through the protected `Scope` property.
Implement `ArrangeCoreAsync` to seed data, configure stubs, or prepare other domain state. Call
`EnsureNotArranged` in configuration methods before changing state.

<!-- snippet: tests/TestApi.IntegrationTests/Scenarios/ProductScenario.cs#docs-domain-scenario-base -->
```csharp
internal sealed class ProductScenario : Scenario<Program>
{
    private readonly TestApiFactory _factory;
    private readonly List<Product> _products = [];

    public ProductScenario(
        TestApiFactory factory,
        TestScenarioScope<Program> scope)
        : base(scope)
    {
        _factory = factory;
    }

    public const string CollectionUri = "/api/products";

    public ProductScenario WithExistingProduct(
        int id,
        string name,
        decimal price)
    {
        EnsureNotArranged();
        _products.Add(new Product
        {
            Id = id,
            Name = name,
            Price = price
        });
        return this;
    }

    public static string ResourceUri(int id) => $"{CollectionUri}/{id}";

    protected override Task ArrangeCoreAsync(CancellationToken cancellationToken) =>
        _factory.Database(Scope)
            .Seed(_products.ToArray())
            .ExecuteAsync(cancellationToken);
}
```
<!-- end-snippet -->

`Arrange()` freezes configuration immediately and returns a request builder that runs
`ArrangeCoreAsync` before sending its request. Existing fluent authentication and response
assertions remain available. `ArrangeAsync(token)` freezes configuration and applies setup
immediately without requiring an HTTP request, so subsequent operations can share the same state:

<!-- snippet: tests/TestApi.IntegrationTests/ScenarioPrimitiveTests.cs#docs-domain-scenario-workflow -->
```csharp
[Fact]
public Task Arranged_domain_state_supports_multiple_requests_in_one_scope() =>
    RunAsync(async (scope, cancellationToken) =>
    {
        var products = new ProductScenario(Factory, scope)
            .WithExistingProduct(861, "Desk lamp", 34.95m);
        await products.ArrangeAsync(cancellationToken);

        using var client = scope.CreateAuthenticatedClient();
        using var first = await client.GetAsync(ProductScenario.ResourceUri(861), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var deleted = await client.DeleteAsync(ProductScenario.ResourceUri(861), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var missing = await client.GetAsync(ProductScenario.ResourceUri(861), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    });
```
<!-- end-snippet -->

A domain scenario can be arranged only once. Calling either arrangement method prevents subsequent
arrangement and guarded configuration changes, including after cancellation or failure. Calling
`Arrange()` consumes the scenario even when its builder is never executed. The base checks
cancellation before domain setup and passes the token to `ArrangeCoreAsync`; derived implementations
must pass it to their asynchronous operations. Instances are mutable and must not be used concurrently.

The caller owns the scope and must keep it alive for setup and all subsequent operations. A domain
scenario never disposes the scope. Run the workflow through `RunInTestScenarioScopeAsync` or
`ScopedTest.RunAsync` to capture failures before cleanup. Arrangement called directly on a manually
owned scope follows that scope's existing diagnostics behavior.

## Scoped test classes

`ScopedTest<TEntryPoint, TFactory>` is an optional, test-framework-independent base. It exposes the
borrowed concrete `Factory` and provides protected `RunAsync` overloads for callbacks with and
without a returned value, with scope overrides available through separate overloads. Each call
delegates to the existing factory runner and owns a fresh scope for the complete callback lifetime.

Supply the test framework's cancellation token to the base constructor. Without a token, runs are
non-cancelable. Framework fixture registration remains in the test project; the base does not own
or dispose the shared factory. This xUnit example replaces a repeated factory field and runner:

<!-- snippet: tests/TestApi.IntegrationTests/ProductScenarioExamples.cs#docs-scoped-test-base -->
```csharp
public sealed class ProductScenarioExamples : ScopedTest<Program, TestApiFactory>, IClassFixture<TestApiFactory>
{
    public ProductScenarioExamples(TestApiFactory factory)
        : base(factory, TestContext.Current.CancellationToken)
    {
    }

    [Fact]
    public Task Domain_scenario_can_arrange_an_existing_product() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(Factory, scope)
                .WithExistingProduct(841, "Desk lamp", 34.95m);

            using var result = await products.Arrange()
                .AsUser(user => user.WithName("Product reader"))
                .Get(ProductScenario.ResourceUri(841))
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.OK)
                .HaveJsonBodyAsync(
                    new ProductResponse(841, "Desk lamp", 34.95m),
                    cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Domain_scenario_can_compose_multiple_arrangements() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(Factory, scope)
                .WithExistingProduct(852, "Mouse", 45m)
                .WithExistingProduct(851, "Keyboard", 120m);

            using var result = await products.Arrange()
                .AsUser(user => user.WithName("Catalog reader"))
                .Get(ProductScenario.CollectionUri)
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.OK)
                .HaveJsonBodyAsync(
                    new[]
                    {
                        new ProductResponse(851, "Keyboard", 120m),
                        new ProductResponse(852, "Mouse", 45m)
                    },
                    cancellationToken: cancellationToken);
        });

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
```
<!-- end-snippet -->

`RunAsync` preserves the factory's serialization gate, failure diagnostics, and cleanup behavior.
The result-returning overload finishes cleanup before returning its value. Return detached data
such as records or identifiers; scopes, services, and environment resources cannot outlive the
callback. The base stores no active scope between runs. A test scope owns an application host and
test resources, and is broader than a DI service scope.

See the API reference for <xref:XBullet.EasyTesting.Hosting.Scenario`1> and
<xref:XBullet.EasyTesting.Hosting.ScopedTest`2>.

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
