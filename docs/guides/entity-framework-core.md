# Test with Entity Framework Core

`XBullet.EasyTesting.EntityFrameworkCore` adds EF-backed test factories, scoped database actions,
scenario isolation, provider replacement, and cleanup diagnostics. Use it when HTTP behavior and
database state belong in the same integration-test scenario.

## Install

```shell
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
```

The test project must also reference the selected EF Core provider.

## Choose a provider

| Provider | Use it when | Important limitation |
| --- | --- | --- |
| EF Core in-memory | Fast tests need change tracking and basic query behavior but not relational semantics | No relational constraints, transactions, provider SQL, or database-specific behavior |
| SQLite in-memory | Tests need relational constraints and transactions with inexpensive per-scenario databases | SQLite behavior and SQL still differ from SQL Server, PostgreSQL, and other production providers |
| Production provider with Testcontainers | Provider-specific SQL, migrations, indexes, constraints, or concurrency matter | Requires container runtime and slower resource startup |
| Existing test database | Infrastructure manages an isolated database outside the test process | Cleanup, parallelism, and ownership must be explicitly designed |

Do not use the in-memory provider to prove relational behavior.

## Create a factory

For fast non-relational tests, derive from the in-memory factory:

<!-- snippet: tests/TestApi.IntegrationTests/InMemoryDatabaseTests.cs#docs-in-memory-ef-factory -->
```csharp
public sealed class InMemoryTestApiFactory
    : InMemoryEntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
}
```
<!-- end-snippet -->

For SQLite or another relational provider, derive from
`EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` and override
`ConfigureDatabaseServices`. The factory removes both the application's `TDbContext` and
`IDbContextFactory<TDbContext>` registrations before adding the test provider.

Use `StartupEntityFrameworkWebApplicationFactory<TStartup, TDbContext>` when the test must run a
Startup pipeline without executing `Program.Main`.

## Seed, query, update, and transact

Use `Database(scope)` to compose actions for one scenario. Convenience methods are available for
simple queries and updates:

<!-- snippet: tests/TestApi.IntegrationTests/DatabaseControllerTests.cs#docs-ef-database-actions -->
```csharp
[Fact]
public Task Generic_database_actions_support_seed_query_update_and_transactions() =>
    Run(async (scope, cancellationToken) =>
    {
        await _factory.Database(scope)
            .Seed(new Product { Id = 301, Name = "Original", Price = 10m })
            .ExecuteAsync(cancellationToken);

        var original = await _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products
                .AsNoTracking()
                .SingleAsync(product => product.Id == 301, token),
            cancellationToken);

        await _factory.ExecuteDatabaseAsync(
            scope,
            async (database, token) =>
            {
                var product = await database.Products.SingleAsync(item => item.Id == 301, token);
                product.Name = "Updated";
            },
            cancellationToken);

        await _factory.Database(scope)
            .Apply(database =>
                database.Products.Add(new Product
                {
                    Id = 302,
                    Name = "Transactional",
                    Price = 20m
                }))
            .InTransaction()
            .ExecuteAsync(cancellationToken);

        var products = await _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products
                .AsNoTracking()
                .OrderBy(product => product.Id)
                .ToListAsync(token),
            cancellationToken);

        Assert.Equal("Original", original.Name);
        Assert.Collection(
            products,
            product => Assert.Equal("Updated", product.Name),
            product => Assert.Equal("Transactional", product.Name));
    });
```
<!-- end-snippet -->

`Seed`, `Apply`, `EnsureCreated`, `RecreateDatabaseWith`, and `InTransaction` execute when
`ExecuteAsync` is called. A database scenario executes once. Query delegates receive the context
and cancellation token inside a correctly scoped service provider.

For a complete HTTP flow, arrange database state inside `scope.Scenario().Arrange(...)`, as shown in
the [realistic controller scenario](../getting-started/first-controller-test.md#arrange-data-and-assert-the-response).

## Test an authenticated CRUD lifecycle

The following canonical test creates, reads, updates, and deletes one product through authenticated
HTTP requests, then queries the isolated scenario database to verify the final state:

<!-- snippet: tests/TestApi.IntegrationTests/DocumentationExamples.cs#docs-authenticated-crud -->
```csharp
public sealed class AuthenticatedCrudDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public AuthenticatedCrudDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_client_can_create_read_update_and_delete_a_product()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var client = scope.Client()
            .AsUser(user => user.WithName("CRUD tester"))
            .Build();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/products",
            new ProductRequest("Webcam", 79.95m),
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var resourceUri = $"/api/products/{created.Id}";
        using var readResponse = await client.GetAsync(resourceUri, cancellationToken);
        var read = await readResponse.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(created, read);

        using var updateResponse = await client.PutAsJsonAsync(
            resourceUri,
            new ProductRequest("Conference webcam", 99.95m),
            cancellationToken);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductResponse>(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Conference webcam", updated!.Name);

        using var deleteResponse = await client.DeleteAsync(resourceUri, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var remainingProducts = await _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);
        Assert.Equal(0, remainingProducts);
    }

    private sealed record ProductRequest(string Name, decimal Price);

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
```
<!-- end-snippet -->

Keep operation-specific failure cases in separate tests. Typical companion coverage includes an
unknown identifier returning `404 Not Found`, invalid input returning `400 Bad Request`, and a
forbidden or anonymous caller leaving database state unchanged.

## Scenario database isolation

The default per-scenario lifecycle is:

1. Create the scenario child host and provider registrations.
2. Call `EnsureDeleted`.
3. Call `EnsureCreated`.
4. Run arrangements, requests, assertions, and database actions.
5. Call the cleanup hook before disposing the child host.

Override `ConfigureScenarioDatabaseServices` when every scope needs its own connection, database
name, schema, or container-provided endpoint. Attach scenario-created connections to
`TestScenarioContext.DisposeWithScenario`.

Override `InitializeScenarioDatabaseAsync` and `CleanupScenarioDatabaseAsync` when the application
requires migrations, schema verification, a database template, or another lifecycle. Do not combine
shared mutable databases with parallel factory instances unless the isolation boundary is explicit.

Each isolated database name, connection, or schema can produce a distinct EF Core option shape and
internal service provider. The EF-backed test factories therefore suppress
`CoreEventId.ManyServiceProvidersCreatedWarning` for factory and scenario contexts. This expected
test-host behavior remains safe when a suite promotes EF Core warnings to exceptions; every other
configured EF Core warning keeps its configured behavior.

## Migrations and production-like tests

When migration behavior matters, replace default `EnsureCreated` initialization with the
application's migration path. A useful production-like test should verify at least:

- The migration set applies to an empty database.
- The application starts against the migrated schema.
- Provider-specific queries, constraints, and transactions behave as expected.
- Each test receives an isolated database or deterministic cleanup.

Use `XBullet.EasyTesting.Testcontainers` when the production provider must run in a real service.

## Cleanup and diagnostics

SQLite file cleanup clears connection pools and retries transient lock failures. A terminal cleanup
failure adds `SqliteDatabaseCleanupDiagnostics` to the exception's `Data` dictionary under
`SqliteDatabaseCleanupDiagnostics.ExceptionDataKey`. It reports the data source, provider, attempt
count, and related cleanup context without exposing credentials.

Database provider details can also appear in `TestScenarioDiagnostics` when a scenario delegate
fails. Diagnostics are captured before cleanup so the provider state is still available.

## Related documentation

- [Wait for background database writes](eventual-assertions.md#wait-for-persisted-state)
- [Scenarios and isolation](../concepts/scenarios-and-isolation.md)
- [Resources and cleanup](../concepts/resources-and-cleanup.md)
- [Testcontainers package guide](../../src/XBullet.EasyTesting.Testcontainers/README.md)
- [Entity Framework Core package README](../../src/XBullet.EasyTesting.EntityFrameworkCore/README.md)
- [Entity Framework Core API reference](../api/packages/xbullet-easytesting-entityframeworkcore.md)
