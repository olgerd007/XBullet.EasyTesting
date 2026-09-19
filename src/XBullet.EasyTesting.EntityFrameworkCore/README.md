# XBullet.EasyTesting.EntityFrameworkCore

Entity Framework Core database actions and test-factory support for `XBullet.EasyTesting` integration tests.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
```

Derive your test factory from `EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` to configure an isolated database for each scenario. Tests can seed, query, and execute scoped database actions through the factory while requests still run through the in-memory ASP.NET Core host.

For fast tests that do not require relational database behavior, derive from
`InMemoryEntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` instead. It registers
the EF Core in-memory provider automatically and creates an isolated database for each test
scenario:

```csharp
public sealed class TestApiFactory
    : InMemoryEntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
}
```

The EF Core in-memory provider does not enforce relational constraints or support transactions.
Use `EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` with SQLite or the same
relational provider used in production when those behaviors are relevant to the test.

For an application that exposes `IntegrationTestStartup` and must not execute `Program.Main`, use
the equivalent Startup-based host. It runs directly on `TestServer` and keeps the same database and
scenario APIs:

```csharp
public sealed class StartupTestHost
    : StartupEntityFrameworkWebApplicationFactory<IntegrationTestStartup, AppDbContext>
{
    protected override void ConfigureDatabaseServices(IServiceCollection services) =>
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
}
```

```csharp
var product = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products
        .SingleAsync(item => item.Id == 42, cancellationToken));
```

`AddDbContextFactory<TContext>` is supported as a test registration. Database replacement removes
both the application's context and `IDbContextFactory<TContext>` registrations before adding the
test provider.

The default per-scenario lifecycle remains `EnsureDeleted` followed by `EnsureCreated`. Override
`InitializeScenarioDatabaseAsync` and `CleanupScenarioDatabaseAsync` to invoke application
migrations, schema verification, template restore, or another initializer. For a single database
arrangement, use `Database().RecreateDatabaseWith(...)`.

SQLite file cleanup clears connection pools, retries transient lock failures, and adds a
`SqliteDatabaseCleanupDiagnostics` value to the terminal exception's `Data` dictionary under
`SqliteDatabaseCleanupDiagnostics.ExceptionDataKey`.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for factory setup and isolation examples.
