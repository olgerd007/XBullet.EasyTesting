using System.Net;
using System.Net.Http.Json;
using TestStartupApi.Controllers;
using TestStartupApi.External;
using TestStartupApi.IntegrationTests.Scenarios;
using TestStartupApi.IntegrationTests.Stubs;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestStartupApi.IntegrationTests;

public sealed class OrderShipmentScenarioTests : ScopedTest<Startup, StartupApiFactory>, IClassFixture<StartupApiFactory>
{
    public OrderShipmentScenarioTests(StartupApiFactory factory)
        : base(factory, TestContext.Current.CancellationToken)
    {
    }

    [Fact]
    public Task Create_shipment_scenario_persists_details_and_calls_post_provider() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(Factory, scope)
                .WithStoredOrder(
                    61,
                    "ext-61",
                    "customer-61",
                    80m,
                    new ExternalOrderItem("book-01", 2, 25m),
                    new ExternalOrderItem("pen-02", 3, 10m))
                .WithPostProviderShipment(
                    "ext-61",
                    "post-9001",
                    "Contoso Post",
                    "TRACK-9001");
            var request = new OrderShipmentsController.CreateShipmentRequest(
                "Kyiv, Ukraine",
                [
                    new OrderShipmentsController.ShipmentLineRequest("book-01", 1),
                    new OrderShipmentsController.ShipmentLineRequest("pen-02", 3)
                ]);

            using var result = await orders.Arrange()
                .AsApiUser()
                .PostJson(OrderScenario.ShipmentsUri(61), request)
                .ExecuteAsync(cancellationToken);
            var response = await result.Response.Content.ReadFromJsonAsync<
                OrderShipmentsController.ShipmentResponse>(cancellationToken);

            Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
            Assert.NotNull(response);
            Assert.Equal("post-9001", response.ProviderShipmentId);
            Assert.Equal("TRACK-9001", response.TrackingNumber);
            Assert.Equal(2, response.Details.Count);

            var stored = await orders.GetStoredShipmentAsync(61, cancellationToken);
            Assert.Equal("Created", stored.Status.ToString());
            Assert.Collection(
                stored.Details.OrderBy(detail => detail.OrderItem.Sku, StringComparer.Ordinal),
                detail =>
                {
                    Assert.Equal("book-01", detail.OrderItem.Sku);
                    Assert.Equal(1, detail.Quantity);
                },
                detail =>
                {
                    Assert.Equal("pen-02", detail.OrderItem.Sku);
                    Assert.Equal(3, detail.Quantity);
                });

            orders.VerifyPostProviderCalled("ext-61");
            var providerRequest = Assert.IsType<PostShipmentRequest>(Factory.PostProvider.Calls
                .Single(call => call.Operation == PostProviderOperation.CreateShipment)
                .Request);
            Assert.Equal("Kyiv, Ukraine", providerRequest.Destination);
            Assert.Equal(2, providerRequest.Items.Count);
        });

    [Fact]
    public Task Shipment_exceeding_order_quantity_does_not_call_post_provider() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var orders = new OrderScenario(Factory, scope)
                .WithStoredOrder(
                    62,
                    "ext-62",
                    items: new ExternalOrderItem("book-01", 1, 25m));
            var request = new OrderShipmentsController.CreateShipmentRequest(
                "Kyiv, Ukraine",
                [new OrderShipmentsController.ShipmentLineRequest("book-01", 2)]);

            using var result = await orders.Arrange()
                .AsApiUser()
                .PostJson(OrderScenario.ShipmentsUri(62), request)
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Conflict, result.Response.StatusCode);
            orders.VerifyPostProviderCalled("ext-62", expectedCount: 0);
        });
}
