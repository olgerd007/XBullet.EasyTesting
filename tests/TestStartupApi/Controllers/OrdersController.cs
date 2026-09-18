using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TestStartupApi.Data;
using TestStartupApi.External;
using TestStartupApi.Features;
using TestStartupApi.Messaging;
using TestStartupApi.Models;

namespace TestStartupApi.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    OrdersDbContext database,
    IExternalOrdersClient externalOrders,
    IKafkaPublisher kafka,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<FeatureOptions> features,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("{id:long}")]
    public async Task<ActionResult<OrderResponse>> Get(
        long id,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    [HttpPost("import/{externalId}")]
    [Authorize(Policy = AuthorizationPolicies.OrdersWrite)]
    public async Task<ActionResult<OrderResponse>> Import(
        string externalId,
        CancellationToken cancellationToken)
    {
        if (await database.Orders.AnyAsync(
            item => item.ExternalId == externalId,
            cancellationToken))
        {
            return Conflict();
        }

        ExternalOrder? externalOrder;
        try
        {
            externalOrder = await externalOrders.GetAsync(externalId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        if (externalOrder is null)
        {
            return NotFound();
        }

        var order = new ImportedOrder
        {
            ExternalId = externalOrder.Id,
            CustomerId = externalOrder.CustomerId,
            Description = externalOrder.Description,
            Total = externalOrder.Total,
            ImportedAt = timeProvider.GetUtcNow(),
            ImportedBy = User.FindFirst("oid")?.Value ?? User.Identity?.Name ?? "unknown",
            Items = features.Value.OrderItems
                ? (externalOrder.Items ?? [])
                    .Select(item => new ImportedOrderItem
                    {
                        Sku = item.Sku,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice
                    })
                    .ToList()
                : []
        };
        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);

        var integrationEvent = new OrderImportedEvent(
            order.Id,
            order.ExternalId,
            order.CustomerId,
            order.Total,
            order.ImportedAt,
            ToItemResponses(order.Items));
        await kafka.PublishAsync(
            kafkaOptions.Value.OrdersImportedTopic,
            order.CustomerId,
            integrationEvent,
            new Dictionary<string, string>
            {
                ["event-type"] = "order-imported",
                ["source"] = "test-startup-api"
            },
            cancellationToken);

        var response = ToResponse(order);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, response);
    }

    private static OrderResponse ToResponse(ImportedOrder order) =>
        new(
            order.Id,
            order.ExternalId,
            order.CustomerId,
            order.Description,
            order.Total,
            order.ImportedAt,
            ToItemResponses(order.Items));

    private static IReadOnlyList<OrderItemResponse> ToItemResponses(
        IEnumerable<ImportedOrderItem> items) =>
        items
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .Select(item => new OrderItemResponse(
                item.Sku,
                item.Quantity,
                item.UnitPrice,
                item.Quantity * item.UnitPrice))
            .ToArray();

    public sealed record OrderResponse(
        long Id,
        string ExternalId,
        string CustomerId,
        string Description,
        decimal Total,
        DateTimeOffset ImportedAt,
        IReadOnlyList<OrderItemResponse> Items);

    public sealed record OrderItemResponse(
        string Sku,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal);

    public sealed record OrderImportedEvent(
        long OrderId,
        string ExternalId,
        string CustomerId,
        decimal Total,
        DateTimeOffset ImportedAt,
        IReadOnlyList<OrderItemResponse> Items);
}
