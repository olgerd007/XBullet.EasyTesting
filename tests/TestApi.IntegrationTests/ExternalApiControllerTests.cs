using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ExternalApiControllerTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ExternalApiControllerTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Controller_calls_external_api_and_saves_the_result_to_database() =>
        Run(async (scope, cancellationToken) =>
        {
            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/products/701")
                .RespondJson(new { Id = 701, Name = "External Keyboard", Price = 149.95m });
            using var client = scope.Client()
                .AsUser(TestUser.Create(name: "Importer"))
                .Build();

            using var response = await client.PostAsync(
                "/api/products/import/701",
                content: null,
                cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);
            var persisted = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.Products
                    .AsNoTracking()
                    .SingleAsync(product => product.Id == 701, token),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new ProductResponse(701, "External Keyboard", 149.95m), body);
            Assert.Equal("External Keyboard", persisted.Name);
            Assert.Equal(149.95m, persisted.Price);
            var request = Assert.Single(_factory.ExternalCatalog.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/products/701", request.RequestUri!.PathAndQuery);
        });

    [Fact]
    public Task External_not_found_response_does_not_save_a_product() =>
        Run(async (scope, cancellationToken) =>
        {
            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/products/702")
                .Respond(HttpStatusCode.NotFound);
            using var client = scope.CreateAuthenticatedClient();

            using var response = await client.PostAsync(
                "/api/products/import/702",
                content: null,
                cancellationToken);
            var productCount = await CountProductsAsync(scope, cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, productCount);
            Assert.Equal(1, _factory.ExternalCatalog.CallCount);
        });

    [Fact]
    public Task External_failure_returns_bad_gateway_without_saving_a_product() =>
        Run(async (scope, cancellationToken) =>
        {
            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/products/703")
                .Respond(HttpStatusCode.ServiceUnavailable);
            using var client = scope.CreateAuthenticatedClient();

            using var response = await client.PostAsync(
                "/api/products/import/703",
                content: null,
                cancellationToken);
            var productCount = await CountProductsAsync(scope, cancellationToken);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(0, productCount);
            Assert.Equal(1, _factory.ExternalCatalog.CallCount);
        });

    [Fact]
    public Task Anonymous_request_is_rejected_before_calling_external_api() =>
        Run(async (scope, cancellationToken) =>
        {
            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/products/704")
                .RespondJson(new { Id = 704, Name = "Should not be imported", Price = 1m });
            using var client = scope.CreateAnonymousClient();

            using var response = await client.PostAsync(
                "/api/products/import/704",
                content: null,
                cancellationToken);
            var productCount = await CountProductsAsync(scope, cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, _factory.ExternalCatalog.CallCount);
            Assert.Equal(0, productCount);
        });

    [Fact]
    public Task Catalog_sync_calls_external_api_and_upserts_products_with_an_audit_record() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new TestApi.Models.Product
                {
                    Id = 801,
                    Name = "Old Keyboard",
                    Price = 99m
                })
                .ExecuteAsync(cancellationToken);
            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/products")
                .WithQueryParameter("category", "computer accessories")
                .WithQueryParameter("limit", "10")
                .RespondJson(new[]
                {
                    new { Id = 801, Name = "Updated Keyboard", Price = 119.95m },
                    new { Id = 802, Name = "Ergonomic Mouse", Price = 69.50m }
                });
            using var client = scope.CreateAuthenticatedClient();

            using var response = await client.PostAsJsonAsync(
                "/api/products/sync",
                new { Category = "computer accessories", Limit = 10 },
                cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<CatalogSyncResponse>(
                cancellationToken);
            var result = await _factory.QueryDatabaseAsync(
                scope,
                async (database, token) => new
                {
                    Products = await database.Products
                        .AsNoTracking()
                        .OrderBy(product => product.Id)
                        .ToListAsync(token),
                    SyncRun = await database.CatalogSyncRuns
                        .AsNoTracking()
                        .SingleAsync(token)
                },
                cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(body);
            Assert.Equal("Completed", body.Status);
            Assert.Equal(1, body.CreatedCount);
            Assert.Equal(1, body.UpdatedCount);
            Assert.Equal(result.SyncRun.Id, body.SyncRunId);
            Assert.Collection(
                result.Products,
                product => Assert.Equal("Updated Keyboard", product.Name),
                product => Assert.Equal("Ergonomic Mouse", product.Name));
            Assert.Equal(TestApi.Models.CatalogSyncStatus.Completed, result.SyncRun.Status);
            Assert.NotNull(result.SyncRun.CompletedAt);
            Assert.Equal(1, _factory.ExternalCatalog.CallCount);
        });

    [Fact]
    public Task Failed_catalog_sync_saves_a_failed_audit_record_without_changing_products() =>
        Run(async (scope, cancellationToken) =>
        {
            _factory.ExternalCatalog
                .When(HttpMethod.Get, "/products")
                .WithQueryParameter("category", "unavailable")
                .Respond(HttpStatusCode.ServiceUnavailable);
            using var client = scope.CreateAuthenticatedClient();

            using var response = await client.PostAsJsonAsync(
                "/api/products/sync",
                new { Category = "unavailable", Limit = 25 },
                cancellationToken);
            var result = await _factory.QueryDatabaseAsync(
                scope,
                async (database, token) => new
                {
                    ProductCount = await database.Products.CountAsync(token),
                    SyncRun = await database.CatalogSyncRuns.AsNoTracking().SingleAsync(token)
                },
                cancellationToken);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(0, result.ProductCount);
            Assert.Equal(TestApi.Models.CatalogSyncStatus.Failed, result.SyncRun.Status);
            Assert.Equal("The external catalog request failed.", result.SyncRun.FailureReason);
            Assert.NotNull(result.SyncRun.CompletedAt);
        });

    [Fact]
    public Task Invalid_catalog_sync_is_rejected_before_external_or_database_work() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAuthenticatedClient();

            using var response = await client.PostAsJsonAsync(
                "/api/products/sync",
                new { Category = string.Empty, Limit = 101 },
                cancellationToken);
            var syncRunCount = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.CatalogSyncRuns.CountAsync(token),
                cancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, _factory.ExternalCatalog.CallCount);
            Assert.Equal(0, syncRunCount);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private Task<int> CountProductsAsync(
        TestScenarioScope<Program> scope,
        CancellationToken cancellationToken) =>
        _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);

    private sealed record ProductResponse(int Id, string Name, decimal Price);

    private sealed record CatalogSyncResponse(
        long SyncRunId,
        string Status,
        int CreatedCount,
        int UpdatedCount);
}
