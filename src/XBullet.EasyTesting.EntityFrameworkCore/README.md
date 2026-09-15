# XBullet.EasyTesting.EntityFrameworkCore

Entity Framework Core database actions and test-factory support for `XBullet.EasyTesting` integration tests.

The package targets .NET 8 and .NET 10.

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

```csharp
var product = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products
        .SingleAsync(item => item.Id == 42, cancellationToken));
```

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for factory setup and isolation examples.
