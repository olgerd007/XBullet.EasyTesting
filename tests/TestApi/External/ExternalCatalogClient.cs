using System.Net;
using System.Net.Http.Json;

namespace TestApi.External;

public sealed class ExternalCatalogClient(HttpClient httpClient) : IExternalCatalogClient
{
    public async Task<ExternalCatalogProduct?> GetProductAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"products/{productId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExternalCatalogProduct>(cancellationToken);
    }
}
