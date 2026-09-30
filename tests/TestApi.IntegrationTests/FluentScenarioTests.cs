using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TestApi.Models;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Messaging;
using XBullet.EasyTesting.Snapshots;
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
    public Task Scenario_can_record_post_json_request_body() =>
        Run(async (scope, cancellationToken) =>
        {
            using var result = await scope.SnapshotScenario(options =>
                options.Request.IncludeHeaders = false)
                .AsUser(user => user.WithName("Order publisher"))
                .PostJson(
                    "/api/publishing/kafka/orders",
                    new PublishOrderRequest(43, "customer-8", 225.50m))
                .ExecuteAsync(cancellationToken);

            var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
                result.Response,
                cancellationToken: cancellationToken);

            var requestBody = Assert.IsType<JsonElement>(snapshot.Request?.Body);
            Assert.Null(snapshot.Request?.Headers);
            Assert.Equal(43, requestBody.GetProperty("orderId").GetInt32());
            Assert.Equal("customer-8", requestBody.GetProperty("customerId").GetString());
            Assert.Equal(225.50m, requestBody.GetProperty("total").GetDecimal());
        });

    [Fact]
    public Task Scenario_json_request_accepts_per_request_serializer_options() =>
        Run(async (scope, cancellationToken) =>
        {
            var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            serializerOptions.Converters.Add(new JsonStringEnumConverter());

            using var result = await scope.SnapshotScenario(options =>
                options.Request.IncludeHeaders = false)
                .WithJsonOptions(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                .AsUser(user => user.WithName("Order publisher"))
                .PostJson(
                    "/api/publishing/kafka/orders",
                    new PublishOrderWithPriorityRequest(
                        44,
                        "customer-9",
                        325.50m,
                        OrderPriority.High),
                    serializerOptions)
                .ExecuteAsync(cancellationToken);

            var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
                result.Response,
                cancellationToken: cancellationToken);

            var requestBody = Assert.IsType<JsonElement>(snapshot.Request?.Body);
            Assert.Equal("High", requestBody.GetProperty("priority").GetString());
        });

    [Fact]
    public Task Scenario_json_requests_use_configured_serializer_options() =>
        Run(async (scope, cancellationToken) =>
        {
            var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            serializerOptions.Converters.Add(new JsonStringEnumConverter());

            using var result = await scope.SnapshotScenario(options =>
                options.Request.IncludeHeaders = false)
                .WithJsonOptions(serializerOptions)
                .AsUser(user => user.WithName("Order publisher"))
                .PostJson(
                    "/api/publishing/kafka/orders",
                    new PublishOrderWithPriorityRequest(
                        45,
                        "customer-10",
                        425.50m,
                        OrderPriority.High))
                .ExecuteAsync(cancellationToken);

            var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
                result.Response,
                cancellationToken: cancellationToken);

            var requestBody = Assert.IsType<JsonElement>(snapshot.Request?.Body);
            Assert.Equal("High", requestBody.GetProperty("priority").GetString());
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

    private sealed record PublishOrderWithPriorityRequest(
        int OrderId,
        string CustomerId,
        decimal Total,
        OrderPriority Priority);

    private enum OrderPriority
    {
        Normal,
        High
    }

    private sealed record PublishReceipt(string Transport, string Destination);

    private sealed record OrderCreatedMessage(int OrderId, string CustomerId, decimal Total);

    private sealed record UpdateProductRequest(string Name, decimal Price);
}
