namespace TestFunctions.External;

public interface IOrderPricingClient
{
    Task<ProductPrice?> GetProductPriceAsync(
        int productId,
        CancellationToken cancellationToken = default);
}

public sealed record ProductPrice(int ProductId, decimal UnitPrice, string Currency);
