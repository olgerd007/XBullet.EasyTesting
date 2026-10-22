using System.Net;
using System.Net.Http.Json;
using TestStartupApi.Controllers;
using TestStartupApi.External;
using TestStartupApi.Features;
using TestStartupApi.IntegrationTests.Scenarios;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestStartupApi.IntegrationTests;

public sealed class OrderWorkflowDocumentationExamples
    : ScopedTest<Startup, StartupApiFactory>, IClassFixture<StartupApiFactory>
{
    public OrderWorkflowDocumentationExamples(StartupApiFactory factory)
        : base(factory, TestContext.Current.CancellationToken)
    {
    }

    #region docs-order-workflow-success
    [Fact]
    public Task Import_returns_the_persisted_order_and_publishes_its_event() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(Factory, scope)
                .WithExternalOrder(
                    "ext-42", "customer-7", "Replacement parts", 125.50m,
                    new ExternalOrderItem("bearing-01", 2, 50m),
                    new ExternalOrderItem("seal-02", 1, 25.50m));

            using var result = await orders.Arrange()
                .AsApiUser()
                .PostJson(OrderScenario.ImportUri("ext-42"), new { })
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
            var response = await result.Response.Content.ReadFromJsonAsync<
                OrdersController.OrderResponse>(cancellationToken);
            Assert.NotNull(response);

            var stored = await orders.GetStoredOrderAsync("ext-42", cancellationToken);
            Assert.Equal(stored.Id, response.Id);
            Assert.Equal("ext-42", response.ExternalId);
            Assert.Equal("customer-7", response.CustomerId);
            Assert.Equal(125.50m, response.Total);
            Assert.Equal(ApiUserScenarioExtensions.DefaultObjectId, stored.ImportedBy);
            Assert.Equal(
                new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero),
                response.ImportedAt);
            Assert.Equal(stored.ImportedAt, response.ImportedAt);
            Assert.Collection(response.Items,
                item => Assert.Equal(
                    new OrdersController.OrderItemResponse("bearing-01", 2, 50m, 100m), item),
                item => Assert.Equal(
                    new OrdersController.OrderItemResponse("seal-02", 1, 25.50m, 25.50m), item));
            Assert.Equal(response.Items.Select(item => item.Sku),
                stored.Items.OrderBy(item => item.Sku, StringComparer.Ordinal)
                    .Select(item => item.Sku));

            orders.VerifyExternalOrderRequested("ext-42");
            orders.VerifyImportedEvent(stored);
        }, scenario => scenario.EnableFeature(FeatureNames.OrderItems));
    #endregion

    #region docs-order-workflow-duplicate
    [Fact]
    public Task Repeating_an_import_does_not_publish_a_second_event() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(Factory, scope)
                .WithExternalOrder("ext-42", "customer-7", "Replacement parts", 125.50m);
            await orders.ArrangeAsync(cancellationToken);
            using var client = scope.Client().AsAzureAdUser(user => user
                .WithObjectId(ApiUserScenarioExtensions.DefaultObjectId)
                .WithScope(AuthorizationScopes.OrdersWrite))
                .WithoutRedirects()
                .Build();

            using var first = await client.PostAsJsonAsync(
                OrderScenario.ImportUri("ext-42"), new { }, cancellationToken);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var repeated = await client.PostAsJsonAsync(
                OrderScenario.ImportUri("ext-42"), new { }, cancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
            Assert.Equal(1, await orders.GetStoredOrderCountAsync(cancellationToken));
            orders.VerifyExternalOrderRequested("ext-42");
            orders.VerifyImportedEvent(
                await orders.GetStoredOrderAsync("ext-42", cancellationToken));
        });
    #endregion

    #region docs-order-workflow-authorization
    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public Task Rejected_callers_cannot_import_or_publish(
        bool authenticated,
        HttpStatusCode expectedStatus) =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(Factory, scope);
            var scenario = orders.Arrange();
            if (authenticated)
            {
                scenario.AsAzureAdUser(user => user
                    .WithObjectId("user-42")
                    .WithScope("profile.read"));
            }
            else
            {
                scenario.AsAnonymous();
            }

            using var result = await scenario
                .PostJson(OrderScenario.ImportUri("ext-42"), new { })
                .ExecuteAsync(cancellationToken);

            Assert.Equal(expectedStatus, result.Response.StatusCode);
            Assert.Equal(0, await orders.GetStoredOrderCountAsync(cancellationToken));
            orders.VerifyExternalOrderRequested("ext-42", expectedCount: 0);
            orders.VerifyNoPublishedEvents();
        });
    #endregion

    #region docs-order-workflow-missing
    [Fact]
    public Task Missing_upstream_order_leaves_no_persisted_order_or_event() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(Factory, scope)
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
    #endregion
}
