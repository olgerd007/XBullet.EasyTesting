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
}
