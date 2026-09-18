namespace TestApi.External;

public interface IExternalCatalogClient
{
    Task<ExternalCatalogProduct?> GetProductAsync(
        int productId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalCatalogProduct>> GetProductsAsync(
        string category,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalCatalogProduct(int Id, string Name, decimal Price);
