using System.Net;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Messaging;
using Microsoft.EntityFrameworkCore;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class FluentScenarioTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public FluentScenarioTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Scenario_can_arrange_authenticate_and_get_a_resource() =>
        Run(async (scope, cancellationToken) =>
        {
            using var result = await scope.Scenario()
                .Arrange(token => _factory.Database(scope)
                    .Seed(new Product { Id = 841, Name = "Desk lamp", Price = 34.95m })
                    .ExecuteAsync(token))
                .AsUser(user => user.WithName("Product reader"))
                .Get("/api/products/841")
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.OK)
                .HaveJsonBodyAsync(
                    new ProductResponse(841, "Desk lamp", 34.95m),
                    cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Scenario_can_post_json_and_capture_the_side_effect() =>
        Run(async (scope, cancellationToken) =>
        {
            using var result = await scope.Scenario()
                .AsUser(user => user.WithName("Order publisher"))
                .PostJson(
                    "/api/publishing/kafka/orders",
                    new PublishOrderRequest(42, "customer-7", 125.50m))
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.Accepted)
                .HaveHeader("Content-Type", "application/json; charset=utf-8")
                .HaveJsonBodyAsync(
                    new PublishReceipt(MessageTransportNames.Kafka, "orders.created"),
                    cancellationToken: cancellationToken);

            _factory.PublishedMessages.Should()
                .ContainSingle(MessageTransportNames.Kafka, "orders.created")
                .HaveHeader("partition-key", "customer-7")
                .HavePayload(new OrderCreatedMessage(42, "customer-7", 125.50m));
        });

    [Fact]
    public async Task Scenario_can_update_and_delete_a_resource()
    {
        await Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 842, Name = "Before", Price = 10m })
                .ExecuteAsync(cancellationToken);
            using var result = await scope.Scenario()
                .AsUser(TestUser.Create("Product editor"))
                .PutJson(
                    "/api/products/842",
                    new UpdateProductRequest("After", 20m))
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.OK)
                .HaveJsonBodyAsync(
                    new ProductResponse(842, "After", 20m),
                    cancellationToken: cancellationToken);
        });

        await Run(async (scope, cancellationToken) =>
        {
            await _factory.Database(scope)
                .Seed(new Product { Id = 842, Name = "Delete me", Price = 20m })
                .ExecuteAsync(cancellationToken);
            using var result = await scope.Scenario()
                .AsUser(TestUser.Create("Product editor"))
                .Delete("/api/products/842")
                .ExecuteAsync(cancellationToken);

            result.Should().HaveStatusCode(HttpStatusCode.NoContent);
            var count = await _factory.QueryDatabaseAsync(
                scope,
                (database, token) => database.Products.CountAsync(token),
                cancellationToken);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public Task Scenario_supports_api_key_and_anonymous_profiles() =>
        Run(async (scope, cancellationToken) =>
        {
            using var authenticated = await scope.Scenario()
                .AsApiKey(apiKey => apiKey.WithKeyId("partner-key"))
                .Get("/api/secure/api-key")
                .ExecuteAsync(cancellationToken);
            using var anonymous = await scope.Scenario()
                .AsAnonymous()
                .Get("/api/secure/me")
                .ExecuteAsync(cancellationToken);

            authenticated.Should().HaveStatusCode(HttpStatusCode.NoContent);
            anonymous.Should().HaveStatusCode(HttpStatusCode.Unauthorized);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed record ProductResponse(int Id, string Name, decimal Price);

    private sealed record PublishOrderRequest(int OrderId, string CustomerId, decimal Total);

    private sealed record PublishReceipt(string Transport, string Destination);

    private sealed record OrderCreatedMessage(int OrderId, string CustomerId, decimal Total);

    private sealed record UpdateProductRequest(string Name, decimal Price);
}
