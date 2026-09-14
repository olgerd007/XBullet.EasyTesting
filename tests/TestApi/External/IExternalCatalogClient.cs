namespace TestApi.External;

public interface IExternalCatalogClient
{
    Task<ExternalCatalogProduct?> GetProductAsync(
        int productId,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalCatalogProduct(int Id, string Name, decimal Price);
