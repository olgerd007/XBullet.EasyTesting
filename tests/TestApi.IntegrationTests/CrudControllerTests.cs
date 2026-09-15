using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
using Microsoft.EntityFrameworkCore;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class CrudControllerTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public CrudControllerTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task List_returns_database_products_in_stable_order() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(
                    new Product { Id = 812, Name = "Mouse", Price = 45m },
                    new Product { Id = 811, Name = "Keyboard", Price = 120m })
                .ExecuteAsync(cancellationToken);
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.GetAsync("/api/products", cancellationToken);
            var products = await response.Content.ReadFromJsonAsync<ProductResponse[]>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(products);
            Assert.Equal(
                [
                    new ProductResponse(811, "Keyboard", 120m),
                    new ProductResponse(812, "Mouse", 45m)
                ],
                products);
        });

    [Fact]
    public Task Create_persists_product_and_returns_created_location() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.PostAsJsonAsync(
                "/api/products",
                new ProductRequest("Webcam", 79.95m),
                cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);
            var persisted = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.Products.AsNoTracking().SingleAsync(token),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(body);
            Assert.True(body.Id > 0);
            Assert.Equal("Webcam", body.Name);
            Assert.Equal(79.95m, body.Price);
            Assert.Equal($"/api/products/{body.Id}", response.Headers.Location?.AbsolutePath);
            Assert.Equal(body.Id, persisted.Id);
            Assert.Equal(body.Name, persisted.Name);
            Assert.Equal(body.Price, persisted.Price);
        });

    [Fact]
    public Task Update_changes_the_persisted_product() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 821, Name = "Old name", Price = 10m })
                .ExecuteAsync(cancellationToken);
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.PutAsJsonAsync(
                "/api/products/821",
                new ProductRequest("Updated name", 25.50m),
                cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);
            var persisted = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.Products.AsNoTracking().SingleAsync(token),
                cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new ProductResponse(821, "Updated name", 25.50m), body);
            Assert.Equal("Updated name", persisted.Name);
            Assert.Equal(25.50m, persisted.Price);
        });

    [Fact]
    public Task Delete_removes_the_product() =>
        Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 831, Name = "Disposable", Price = 5m })
                .ExecuteAsync(cancellationToken);
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.DeleteAsync("/api/products/831", cancellationToken);
            var exists = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.Products.AnyAsync(token),
                cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(exists);
        });

    [Fact]
    public Task Update_and_delete_return_not_found_for_missing_products() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = CreateAuthenticatedClient(scope);

            using var updateResponse = await client.PutAsJsonAsync(
                "/api/products/999",
                new ProductRequest("Missing", 1m),
                cancellationToken);
            using var deleteResponse = await client.DeleteAsync(
                "/api/products/999",
                cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
        });

    [Fact]
    public Task Invalid_create_request_returns_bad_request_without_saving() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = CreateAuthenticatedClient(scope);

            using var response = await client.PostAsJsonAsync(
                "/api/products",
                new ProductRequest(string.Empty, 0m),
                cancellationToken);
            var productCount = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.Products.CountAsync(token),
                cancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, productCount);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private static HttpClient CreateAuthenticatedClient(TestScenarioScope<Program> scope) =>
        scope.Client()
            .AsUser(user => user.WithName("CRUD tester"))
            .Build();

    private sealed record ProductRequest(string Name, decimal Price);

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
