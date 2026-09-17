using System.Net;
using System.Net.Http.Json;

namespace TestFunctions.External;

public sealed class OrderPricingClient(HttpClient httpClient) : IOrderPricingClient
{
    public async Task<ProductPrice?> GetProductPriceAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"products/{productId}/price",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductPrice>(cancellationToken);
    }
}
