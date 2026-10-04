using Microsoft.EntityFrameworkCore;
using TestStartupApi.Controllers;
using TestStartupApi.External;
using TestStartupApi.Models;
using XBullet.EasyTesting.Hosting;

namespace TestStartupApi.IntegrationTests.Scenarios;

internal sealed class OrderScenario : Scenario<Startup>
{
    private readonly StartupApiFactory _factory;
    private readonly Dictionary<string, ExternalOrder?> _externalOrders =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PostShipmentResult> _postShipments =
        new(StringComparer.Ordinal);
    private readonly List<ImportedOrder> _storedOrders = [];

    public OrderScenario(
        StartupApiFactory factory,
        TestScenarioScope<Startup> scope)
        : base(scope)
    {
        _factory = factory;
    }

    public OrderScenario WithExternalOrder(
        string externalId,
        string customerId,
        string description,
        decimal total,
        params ExternalOrderItem[] items)
    {
        EnsureNotArranged();
        ArgumentNullException.ThrowIfNull(items);
        _externalOrders[externalId] = new ExternalOrder(
            externalId,
            customerId,
            description,
            total,
            items);
        return this;
    }

    public OrderScenario WithMissingExternalOrder(string externalId)
    {
        EnsureNotArranged();
        _externalOrders[externalId] = null;
        return this;
    }

    public OrderScenario WithPostProviderShipment(
        string externalOrderId,
        string providerShipmentId,
        string carrier,
        string trackingNumber)
    {
        EnsureNotArranged();
        _postShipments[externalOrderId] = new PostShipmentResult(
            providerShipmentId,
            carrier,
            trackingNumber);
        return this;
    }

    public OrderScenario WithStoredOrder(
        long id,
        string externalId,
        string customerId = "existing-customer",
        decimal total = 10m,
        params ExternalOrderItem[] items)
    {
        EnsureNotArranged();
        ArgumentNullException.ThrowIfNull(items);
        _storedOrders.Add(new ImportedOrder
        {
            Id = id,
            ExternalId = externalId,
            CustomerId = customerId,
            Description = "Existing order",
            Total = total,
            ImportedAt = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero),
            ImportedBy = "existing-user",
            Items = items.Select(item => new ImportedOrderItem
            {
                Sku = item.Sku,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            }).ToList()
        });
        return this;
    }

    protected override Task ArrangeCoreAsync(CancellationToken cancellationToken)
    {
        foreach (var externalOrder in _externalOrders)
        {
            _factory.ExternalOrders.ReturnsGet(externalOrder.Key, externalOrder.Value);
        }

        foreach (var postShipment in _postShipments)
        {
            _factory.PostProvider.ReturnsCreateShipment(postShipment.Key, postShipment.Value);
        }

        return _factory.Database(Scope)
            .Seed(_storedOrders.ToArray())
            .ExecuteAsync(cancellationToken);
    }

    public async Task<ImportedOrder> GetStoredOrderAsync(
        string externalId,
        CancellationToken cancellationToken) =>
        await _factory.QueryDatabaseAsync(
            Scope,
            (database, token) => database.Orders
                .AsNoTracking()
                .Include(order => order.Items)
                .SingleAsync(order => order.ExternalId == externalId, token),
            cancellationToken);

    public Task<int> GetStoredOrderCountAsync(CancellationToken cancellationToken) =>
        _factory.QueryDatabaseAsync(
            Scope,
            (database, token) => database.Orders.CountAsync(token),
            cancellationToken);

    public async Task<OrderShipment> GetStoredShipmentAsync(
        long orderId,
        CancellationToken cancellationToken) =>
        await _factory.QueryDatabaseAsync(
            Scope,
            (database, token) => database.OrderShipments
                .AsNoTracking()
                .Include(shipment => shipment.Details)
                .ThenInclude(detail => detail.OrderItem)
                .SingleAsync(shipment => shipment.ImportedOrderId == orderId, token),
            cancellationToken);

    public void VerifyExternalOrderRequested(string externalId, int expectedCount = 1) =>
        _factory.ExternalOrders.VerifyGetCalled(externalId, expectedCount);

    public void VerifyImportedEvent(ImportedOrder order) =>
        _factory.KafkaMessages.Should()
            .ContainSingle("Kafka", "orders.imported")
            .HaveHeader("partition-key", order.CustomerId)
            .HaveHeader("event-type", "order-imported")
            .HavePayload(new OrdersController.OrderImportedEvent(
                order.Id,
                order.ExternalId,
                order.CustomerId,
                order.Total,
                order.ImportedAt,
                order.Items
                    .OrderBy(item => item.Sku, StringComparer.Ordinal)
                    .Select(item => new OrdersController.OrderItemResponse(
                        item.Sku,
                        item.Quantity,
                        item.UnitPrice,
                        item.Quantity * item.UnitPrice))
                    .ToArray()));

    public void VerifyNoPublishedEvents() =>
        _factory.KafkaMessages.Should().HaveCount(0);

    public void VerifyPostProviderCalled(
        string externalOrderId,
        int expectedCount = 1) =>
        _factory.PostProvider.VerifyCreateShipmentCalled(externalOrderId, expectedCount);

    public static string ImportUri(string externalId) =>
        $"/api/orders/import/{Uri.EscapeDataString(externalId)}";

    public static string ResourceUri(long id) => $"/api/orders/{id}";

    public static string ShipmentsUri(long orderId) => $"/api/orders/{orderId}/shipments";
}
