namespace TestStartupApi.Models;

public sealed class ImportedOrder
{
    public long Id { get; set; }

    public required string ExternalId { get; set; }

    public required string CustomerId { get; set; }

    public required string Description { get; set; }

    public decimal Total { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public required string ImportedBy { get; set; }

    public ICollection<ImportedOrderItem> Items { get; set; } = [];

    public ICollection<OrderShipment> Shipments { get; set; } = [];
}

public sealed class ImportedOrderItem
{
    public long Id { get; set; }

    public long ImportedOrderId { get; set; }

    public ImportedOrder Order { get; set; } = null!;

    public required string Sku { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public ICollection<ShipmentDetail> ShipmentDetails { get; set; } = [];
}

public sealed class OrderShipment
{
    public long Id { get; set; }

    public long ImportedOrderId { get; set; }

    public ImportedOrder Order { get; set; } = null!;

    public required string ProviderShipmentId { get; set; }

    public required string Carrier { get; set; }

    public required string TrackingNumber { get; set; }

    public ShipmentStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ShipmentDetail> Details { get; set; } = [];
}

public sealed class ShipmentDetail
{
    public long Id { get; set; }

    public long OrderShipmentId { get; set; }

    public OrderShipment Shipment { get; set; } = null!;

    public long ImportedOrderItemId { get; set; }

    public ImportedOrderItem OrderItem { get; set; } = null!;

    public int Quantity { get; set; }
}

public enum ShipmentStatus
{
    Created,
    InTransit,
    Delivered,
    Cancelled
}
