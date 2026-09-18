using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Hosting;
using TestStartupApi.Controllers;
using TestStartupApi.External;
using TestStartupApi.Features;
using TestStartupApi.IntegrationTests.Scenarios;
using Xunit;

namespace TestStartupApi.IntegrationTests;

public sealed class OrderRouteTests : IClassFixture<StartupApiFactory>
{
    private readonly StartupApiFactory _factory;

    public OrderRouteTests(StartupApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Import_scenario_stores_the_order_and_publishes_an_event()
    {
        return Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope)
                .WithExternalOrder(
                    "ext-42",
                    "customer-7",
                    "Replacement parts",
                    125.50m,
                    new ExternalOrderItem("bearing-01", 2, 50m),
                    new ExternalOrderItem("seal-02", 1, 25.50m));

            using var result = await orders.Arrange()
                .AsApiUser(user => user.WithScope("profile.read"))
                .PostJson(OrderScenario.ImportUri("ext-42"), new { })
                .ExecuteAsync(cancellationToken);
            var response = await result.Response.Content.ReadFromJsonAsync<
                OrdersController.OrderResponse>(cancellationToken);

            Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
            Assert.NotNull(response);
            Assert.Equal("ext-42", response.ExternalId);
            Assert.Collection(
                response.Items,
                item =>
                {
                    Assert.Equal("bearing-01", item.Sku);
                    Assert.Equal(2, item.Quantity);
                    Assert.Equal(100m, item.LineTotal);
                },
                item =>
                {
                    Assert.Equal("seal-02", item.Sku);
                    Assert.Equal(1, item.Quantity);
                    Assert.Equal(25.50m, item.LineTotal);
                });

            var stored = await orders.GetStoredOrderAsync("ext-42", cancellationToken);
            Assert.Equal(ApiUserScenarioExtensions.DefaultObjectId, stored.ImportedBy);
            Assert.Equal(125.50m, stored.Total);
            Assert.Equal(2, stored.Items.Count);
            orders.VerifyExternalOrderRequested("ext-42");
            orders.VerifyImportedEvent(stored);
        }, scenario => scenario.EnableFeature(FeatureNames.OrderItems));
    }

    [Fact]
    public Task Disabled_order_items_feature_imports_order_without_items() =>
        Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope)
                .WithExternalOrder(
                    "ext-disabled",
                    "customer-8",
                    "Feature-disabled order",
                    15m,
                    new ExternalOrderItem("ignored-01", 1, 15m));

            using var result = await orders.Arrange()
                .AsApiUser()
                .PostJson(OrderScenario.ImportUri("ext-disabled"), new { })
                .ExecuteAsync(cancellationToken);
            var response = await result.Response.Content.ReadFromJsonAsync<
                OrdersController.OrderResponse>(cancellationToken);

            Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
            Assert.NotNull(response);
            Assert.Empty(response.Items);

            var stored = await orders.GetStoredOrderAsync("ext-disabled", cancellationToken);
            Assert.Empty(stored.Items);
            orders.VerifyExternalOrderRequested("ext-disabled");
            orders.VerifyImportedEvent(stored);
        });

    [Fact]
    public Task Authenticated_order_scenario_without_required_scope_is_forbidden() =>
        Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope);

            using var result = await orders.Arrange()
                .AsAzureAdUser(user => user
                    .WithObjectId("user-17")
                    .WithScope("profile.read"))
                .PostJson(OrderScenario.ImportUri("ext-42"), new { })
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, result.Response.StatusCode);
            Assert.Equal(0, await orders.GetStoredOrderCountAsync(cancellationToken));
            orders.VerifyExternalOrderRequested("ext-42", expectedCount: 0);
            orders.VerifyNoPublishedEvents();
        });

    [Fact]
    public Task Missing_external_order_scenario_returns_not_found_without_side_effects() =>
        Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope)
                .WithMissingExternalOrder("missing-42");

            using var result = await orders.Arrange()
                .AsApiUser()
                .PostJson(OrderScenario.ImportUri("missing-42"), new { })
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
            Assert.Equal(0, await orders.GetStoredOrderCountAsync(cancellationToken));
            orders.VerifyExternalOrderRequested("missing-42");
            orders.VerifyNoPublishedEvents();
        });

    [Fact]
    public Task Existing_order_scenario_returns_conflict_without_calling_dependencies() =>
        Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope)
                .WithStoredOrder(41, "ext-41");

            using var result = await orders.Arrange()
                .AsApiUser()
                .PostJson(OrderScenario.ImportUri("ext-41"), new { })
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Conflict, result.Response.StatusCode);
            Assert.Equal(1, await orders.GetStoredOrderCountAsync(cancellationToken));
            orders.VerifyExternalOrderRequested("ext-41", expectedCount: 0);
            orders.VerifyNoPublishedEvents();
        });

    [Fact]
    public Task Stored_order_scenario_returns_arranged_items() =>
        Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope)
                .WithStoredOrder(
                    51,
                    "ext-51",
                    "customer-51",
                    30m,
                    new ExternalOrderItem("cable-01", 3, 10m));

            using var result = await orders.Arrange()
                .AsApiUser()
                .Get(OrderScenario.ResourceUri(51))
                .ExecuteAsync(cancellationToken);
            var response = await result.Response.Content.ReadFromJsonAsync<
                OrdersController.OrderResponse>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
            Assert.NotNull(response);
            var item = Assert.Single(response.Items);
            Assert.Equal("cable-01", item.Sku);
            Assert.Equal(3, item.Quantity);
            Assert.Equal(30m, item.LineTotal);
            orders.VerifyExternalOrderRequested("ext-51", expectedCount: 0);
            orders.VerifyNoPublishedEvents();
        });

    [Fact]
    public Task Anonymous_order_scenario_is_rejected_before_calling_dependencies() =>
        Run(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(_factory, scope);

            using var result = await orders.Arrange()
                .AsAnonymous()
                .PostJson(OrderScenario.ImportUri("ext-42"), new { })
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, result.Response.StatusCode);
            Assert.Equal(0, await orders.GetStoredOrderCountAsync(cancellationToken));
            orders.VerifyExternalOrderRequested("ext-42", expectedCount: 0);
            orders.VerifyNoPublishedEvents();
        });

    private Task Run(
        Func<TestScenarioScope<Startup>, CancellationToken, Task> test,
        Action<TestScenarioScopeBuilder>? configure = null) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            configure,
            cancellationToken: TestContext.Current.CancellationToken);
}
