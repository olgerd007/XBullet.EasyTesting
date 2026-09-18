using System.Net;
using System.Net.Http.Json;

namespace TestStartupApi.External;

public sealed class ExternalOrdersClient(HttpClient httpClient) : IExternalOrdersClient
{
    public async Task<IReadOnlyList<ExternalOrder>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("v1/orders", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ExternalOrder>>(cancellationToken)
            ?? [];
    }

    public async Task<ExternalOrder?> GetAsync(
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(externalId);
        using var response = await httpClient.GetAsync($"v1/orders/{encodedId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExternalOrder>(cancellationToken);
    }

    public async Task<ExternalOrder> CreateAsync(
        ExternalOrderInput order,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "v1/orders",
            order,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredOrderAsync(response, cancellationToken);
    }

    public async Task<ExternalOrder?> UpdateAsync(
        string externalId,
        ExternalOrderInput order,
        CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(externalId);
        using var response = await httpClient.PutAsJsonAsync(
            $"v1/orders/{encodedId}",
            order,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadRequiredOrderAsync(response, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(externalId);
        using var response = await httpClient.DeleteAsync(
            $"v1/orders/{encodedId}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private static async Task<ExternalOrder> ReadRequiredOrderAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<ExternalOrder>(cancellationToken)
        ?? throw new HttpRequestException("The external orders API returned an empty response body.");
}
