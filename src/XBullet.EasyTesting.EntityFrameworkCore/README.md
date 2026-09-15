# XBullet.EasyTesting.EntityFrameworkCore

Entity Framework Core database actions and test-factory support for `XBullet.EasyTesting` integration tests.

The package targets .NET 8 and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
```

Derive your test factory from `EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` to configure an isolated database for each scenario. Tests can seed, query, and execute scoped database actions through the factory while requests still run through the in-memory ASP.NET Core host.

```csharp
var product = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products
        .SingleAsync(item => item.Id == 42, cancellationToken));
```

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for factory setup and isolation examples.
