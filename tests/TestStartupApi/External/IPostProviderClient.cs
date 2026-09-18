namespace TestStartupApi.External;

public interface IPostProviderClient
{
    Task<PostShipmentResult> CreateShipmentAsync(
        PostShipmentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PostShipmentRequest(
    string OrderId,
    string Destination,
    IReadOnlyList<PostShipmentItem> Items);

public sealed record PostShipmentItem(
    string Sku,
    int Quantity);

public sealed record PostShipmentResult(
    string ShipmentId,
    string Carrier,
    string TrackingNumber);
