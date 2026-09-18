using System.Net.Http.Json;

namespace TestStartupApi.External;

public sealed class PostProviderClient(HttpClient httpClient) : IPostProviderClient
{
    public async Task<PostShipmentResult> CreateShipmentAsync(
        PostShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "v1/shipments",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PostShipmentResult>(cancellationToken)
            ?? throw new HttpRequestException(
                "The post provider returned an empty shipment response body.");
    }
}
