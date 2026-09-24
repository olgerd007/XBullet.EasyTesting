using Microsoft.Data.Sqlite;
using XBullet.EasyTesting.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TestApi.Data;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class InMemoryDatabaseTests : IClassFixture<InMemoryTestApiFactory>
{
    private readonly InMemoryTestApiFactory _factory;

    public InMemoryDatabaseTests(InMemoryTestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task In_memory_factory_supports_database_actions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await _factory.InitializeDatabaseAsync(cancellationToken);
        await _factory.RecreateDatabaseAsync(cancellationToken);
        await _factory.SeedDatabaseAsync(
            [new Product { Id = 1001, Name = "In-memory product", Price = 12.50m }],
            cancellationToken);

        var product = await _factory.QueryDatabaseAsync(
            (database, token) => database.Products
                .AsNoTracking()
                .SingleAsync(item => item.Id == 1001, token),
            cancellationToken);

        Assert.Equal("In-memory product", product.Name);
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", await _factory.QueryDatabaseAsync(
            (database, _) => Task.FromResult(database.Database.ProviderName),
            cancellationToken));

        var singleUseScenario = _factory.Database().EnsureCreated();
        await singleUseScenario.ExecuteAsync(cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            singleUseScenario.ExecuteAsync(cancellationToken));
    }

    [Fact]
    public async Task In_memory_scenario_databases_are_isolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var first = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken))
        {
            await _factory.Database(first)
                .Seed(new Product { Id = 1002, Name = "Scoped", Price = 1m })
                .ExecuteAsync(cancellationToken);

            var count = await _factory.QueryDatabaseAsync(
                first,
                (database, token) => database.Products.CountAsync(token),
                cancellationToken);
            Assert.Equal(1, count);
        }

        await using var second = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        var secondCount = await _factory.QueryDatabaseAsync(
            second,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);

        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task Database_cleanup_supports_non_sqlite_and_sqlite_providers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var inMemoryOptions = new DbContextOptionsBuilder<TestApiDbContext>()
            .UseInMemoryDatabase($"cleanup-{Guid.NewGuid():N}")
            .Options;
        await using (var database = new TestApiDbContext(inMemoryOptions))
        {
            await database.Database.EnsureCreatedAsync(cancellationToken);
            await DatabaseCleanup.EnsureDeletedAsync(database, cancellationToken);
        }

        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"xbullet-direct-cleanup-{Guid.NewGuid():N}.db");
        try
        {
            var sqliteOptions = new DbContextOptionsBuilder<TestApiDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var database = new TestApiDbContext(sqliteOptions);
            await database.Database.EnsureCreatedAsync(cancellationToken);
            Assert.True(File.Exists(databasePath));
            await DatabaseCleanup.EnsureDeletedAsync(database, cancellationToken);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Sqlite_cleanup_retries_transient_locks_and_attaches_terminal_diagnostics()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = 0;
        await DatabaseCleanup.EnsureDeletedSqliteAsync(
            "Microsoft.EntityFrameworkCore.Sqlite",
            "retry.db",
            () =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new IOException("locked"))
                    : Task.CompletedTask;
            },
            cancellationToken);
        Assert.Equal(2, attempts);

        var terminal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseCleanup.EnsureDeletedSqliteAsync(
                "Microsoft.EntityFrameworkCore.Sqlite",
                "terminal.db",
                () => Task.FromException(new InvalidOperationException("failed")),
                cancellationToken));
        var diagnostics = Assert.IsType<SqliteDatabaseCleanupDiagnostics>(
            terminal.Data[SqliteDatabaseCleanupDiagnostics.ExceptionDataKey]);
        Assert.Equal(1, diagnostics.Attempts);
        Assert.Equal("terminal.db", diagnostics.DataSource);

        attempts = 0;
        var exhausted = await Assert.ThrowsAsync<IOException>(() =>
            DatabaseCleanup.EnsureDeletedSqliteAsync(
                "Microsoft.EntityFrameworkCore.Sqlite",
                "exhausted.db",
                () =>
                {
                    attempts++;
                    return Task.FromException(new IOException("still locked"));
                },
                cancellationToken));
        Assert.Equal(4, attempts);
        Assert.Equal(
            4,
            Assert.IsType<SqliteDatabaseCleanupDiagnostics>(
                exhausted.Data[SqliteDatabaseCleanupDiagnostics.ExceptionDataKey]).Attempts);

        foreach (var errorCode in new[] { 5, 6 })
        {
            attempts = 0;
            await DatabaseCleanup.EnsureDeletedSqliteAsync(
                "Microsoft.EntityFrameworkCore.Sqlite",
                $"sqlite-{errorCode}.db",
                () =>
                {
                    attempts++;
                    return attempts == 1
                        ? Task.FromException(new SqliteException("busy", errorCode))
                        : Task.CompletedTask;
                },
                cancellationToken);
            Assert.Equal(2, attempts);
        }

        attempts = 0;
        await DatabaseCleanup.EnsureDeletedSqliteAsync(
            "Microsoft.EntityFrameworkCore.Sqlite",
            "inner.db",
            () =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new InvalidOperationException(
                        "wrapped",
                        new UnauthorizedAccessException("locked")))
                    : Task.CompletedTask;
            },
            cancellationToken);
        Assert.Equal(2, attempts);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DatabaseCleanup.EnsureDeletedSqliteAsync(
                null,
                null,
                null!,
                cancellationToken));
    }
}

public sealed class InMemoryTestApiFactory
    : InMemoryEntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
}
