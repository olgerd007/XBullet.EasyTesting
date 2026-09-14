using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Snapshots;
using Microsoft.EntityFrameworkCore;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class DatabaseControllerTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public DatabaseControllerTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Product_controller_reads_an_existing_product_from_the_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.SeedDatabaseAsync(
            [new Product { Id = 101, Name = "Mechanical Keyboard", Price = 129.99m }],
            cancellationToken);
        using var client = _factory.CreateAuthenticatedClient(TestUser.Create());

        using var response = await client.GetAsync("/api/products/101", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ProductResponse(101, "Mechanical Keyboard", 129.99m), body);
    }

    [Fact]
    public async Task Product_controller_returns_not_found_for_an_unknown_product()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(TestUser.Create());

        using var response = await client.GetAsync("/api/products/999", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Product_controller_rejects_anonymous_requests_before_querying_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/api/products/101", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Database_backed_product_response_matches_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.SeedDatabaseAsync(
            [new Product { Id = 202, Name = "USB-C Dock", Price = 89.50m }],
            cancellationToken);
        using var client = _factory.CreateAuthenticatedClient(TestUser.Create());
        using var response = await client.GetAsync("/api/products/202", cancellationToken);

        await response.ShouldMatchControllerSnapshot(cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task Generic_database_actions_support_reset_seed_query_update_and_transactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.RecreateDatabaseAsync(cancellationToken);
        await _factory.SeedDatabaseAsync(
            [new Product { Id = 301, Name = "Original", Price = 10m }],
            cancellationToken);

        var original = await _factory.QueryDatabaseAsync(
            (database, token) => database.Products
                .AsNoTracking()
                .SingleAsync(product => product.Id == 301, token),
            cancellationToken);

        await _factory.ExecuteDatabaseAsync(
            async (database, token) =>
            {
                var product = await database.Products.SingleAsync(item => item.Id == 301, token);
                product.Name = "Updated";
            },
            cancellationToken);

        await _factory.ExecuteInTransactionAsync(
            (database, _) =>
            {
                database.Products.Add(new Product { Id = 302, Name = "Transactional", Price = 20m });
                return Task.CompletedTask;
            },
            cancellationToken);

        var products = await _factory.QueryDatabaseAsync(
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
    }

    [Fact]
    public async Task Fluent_database_scenario_recreates_seeds_and_mutates_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await _factory.Database()
            .Recreate()
            .Seed(new Product { Id = 401, Name = "Fluent", Price = 30m })
            .Apply(database =>
            {
                var product = database.Products.Local.Single(item => item.Id == 401);
                product.Name = "Fluent Updated";
            })
            .InTransaction()
            .ExecuteAsync(cancellationToken);

        var product = await _factory.QueryDatabaseAsync(
            (database, token) => database.Products
                .AsNoTracking()
                .SingleAsync(item => item.Id == 401, token),
            cancellationToken);

        Assert.Equal("Fluent Updated", product.Name);
        Assert.Equal(30m, product.Price);
    }

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
