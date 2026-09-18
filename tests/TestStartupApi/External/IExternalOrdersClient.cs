namespace TestStartupApi.External;

public interface IExternalOrdersClient
{
    Task<IReadOnlyList<ExternalOrder>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<ExternalOrder?> GetAsync(
        string externalId,
        CancellationToken cancellationToken = default);

    Task<ExternalOrder> CreateAsync(
        ExternalOrderInput order,
        CancellationToken cancellationToken = default);

    Task<ExternalOrder?> UpdateAsync(
        string externalId,
        ExternalOrderInput order,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string externalId,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalOrder(
    string Id,
    string CustomerId,
    string Description,
    decimal Total,
    IReadOnlyList<ExternalOrderItem>? Items = null);

public sealed record ExternalOrderItem(
    string Sku,
    int Quantity,
    decimal UnitPrice);

public sealed record ExternalOrderInput(
    string CustomerId,
    string Description,
    decimal Total,
    IReadOnlyList<ExternalOrderItem>? Items = null);
