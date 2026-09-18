using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestStartupApi.Data;
using TestStartupApi.External;
using TestStartupApi.Models;

namespace TestStartupApi.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.OrdersWrite)]
[Route("api/orders/{orderId:long}/shipments")]
public sealed class OrderShipmentsController(
    OrdersDbContext database,
    IPostProviderClient postProvider,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("{shipmentId:long}")]
    public async Task<ActionResult<ShipmentResponse>> Get(
        long orderId,
        long shipmentId,
        CancellationToken cancellationToken)
    {
        var shipment = await database.OrderShipments
            .AsNoTracking()
            .Include(item => item.Details)
            .ThenInclude(item => item.OrderItem)
            .SingleOrDefaultAsync(
                item => item.Id == shipmentId && item.ImportedOrderId == orderId,
                cancellationToken);

        return shipment is null ? NotFound() : Ok(ToResponse(shipment));
    }

    [HttpPost]
    public async Task<ActionResult<ShipmentResponse>> Create(
        long orderId,
        CreateShipmentRequest request,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (request.Items.Count == 0 ||
            request.Items.GroupBy(item => item.Sku, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return BadRequest("Shipment items must contain unique SKUs.");
        }

        var orderItems = order.Items.ToDictionary(item => item.Sku, StringComparer.Ordinal);
        if (request.Items.Any(item => !orderItems.ContainsKey(item.Sku)))
        {
            return BadRequest("Every shipment SKU must belong to the order.");
        }

        var orderItemIds = orderItems.Values.Select(item => item.Id).ToArray();
        var shippedQuantities = await database.ShipmentDetails
            .Where(detail => orderItemIds.Contains(detail.ImportedOrderItemId))
            .GroupBy(detail => detail.ImportedOrderItemId)
            .Select(group => new { OrderItemId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.OrderItemId, item => item.Quantity, cancellationToken);
        if (request.Items.Any(item =>
        {
            var orderItem = orderItems[item.Sku];
            var alreadyShipped = shippedQuantities.GetValueOrDefault(orderItem.Id);
            return alreadyShipped + item.Quantity > orderItem.Quantity;
        }))
        {
            return Conflict("A shipment quantity exceeds the remaining order-item quantity.");
        }

        PostShipmentResult providerShipment;
        try
        {
            providerShipment = await postProvider.CreateShipmentAsync(
                new PostShipmentRequest(
                    order.ExternalId,
                    request.Destination,
                    request.Items
                        .Select(item => new PostShipmentItem(item.Sku, item.Quantity))
                        .ToArray()),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        var shipment = new OrderShipment
        {
            ImportedOrderId = order.Id,
            ProviderShipmentId = providerShipment.ShipmentId,
            Carrier = providerShipment.Carrier,
            TrackingNumber = providerShipment.TrackingNumber,
            Status = ShipmentStatus.Created,
            CreatedAt = timeProvider.GetUtcNow(),
            Details = request.Items.Select(item => new ShipmentDetail
            {
                OrderItem = orderItems[item.Sku],
                Quantity = item.Quantity
            }).ToList()
        };
        database.OrderShipments.Add(shipment);
        await database.SaveChangesAsync(cancellationToken);

        var response = ToResponse(shipment);
        return CreatedAtAction(
            nameof(Get),
            new { orderId = order.Id, shipmentId = shipment.Id },
            response);
    }

    private static ShipmentResponse ToResponse(OrderShipment shipment) =>
        new(
            shipment.Id,
            shipment.ImportedOrderId,
            shipment.ProviderShipmentId,
            shipment.Carrier,
            shipment.TrackingNumber,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.Details
                .OrderBy(item => item.OrderItem.Sku, StringComparer.Ordinal)
                .Select(item => new ShipmentDetailResponse(
                    item.OrderItem.Sku,
                    item.Quantity))
                .ToArray());

    public sealed record CreateShipmentRequest(
        [Required, StringLength(500)] string Destination,
        [Required, MinLength(1)] IReadOnlyList<ShipmentLineRequest> Items);

    public sealed record ShipmentLineRequest(
        [Required, StringLength(100)] string Sku,
        [Range(1, int.MaxValue)] int Quantity);

    public sealed record ShipmentResponse(
        long Id,
        long OrderId,
        string ProviderShipmentId,
        string Carrier,
        string TrackingNumber,
        string Status,
        DateTimeOffset CreatedAt,
        IReadOnlyList<ShipmentDetailResponse> Details);

    public sealed record ShipmentDetailResponse(
        string Sku,
        int Quantity);
}
