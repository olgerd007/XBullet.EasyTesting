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
}

public sealed class InMemoryTestApiFactory
    : InMemoryEntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
}
