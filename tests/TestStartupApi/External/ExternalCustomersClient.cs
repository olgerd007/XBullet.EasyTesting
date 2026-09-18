using System.Net;
using System.Net.Http.Json;

namespace TestStartupApi.External;

public sealed class ExternalCustomersClient(HttpClient httpClient) : IExternalCustomersClient
{
    public async Task<IReadOnlyList<ExternalCustomer>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("v1/customers", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ExternalCustomer>>(cancellationToken)
            ?? [];
    }

    public async Task<ExternalCustomer?> GetAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(customerId);
        using var response = await httpClient.GetAsync(
            $"v1/customers/{encodedId}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExternalCustomer>(cancellationToken);
    }

    public async Task<ExternalCustomer> CreateAsync(
        ExternalCustomerInput customer,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "v1/customers",
            customer,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredCustomerAsync(response, cancellationToken);
    }

    public async Task<ExternalCustomer?> UpdateAsync(
        string customerId,
        ExternalCustomerInput customer,
        CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(customerId);
        using var response = await httpClient.PutAsJsonAsync(
            $"v1/customers/{encodedId}",
            customer,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadRequiredCustomerAsync(response, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        var encodedId = Uri.EscapeDataString(customerId);
        using var response = await httpClient.DeleteAsync(
            $"v1/customers/{encodedId}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private static async Task<ExternalCustomer> ReadRequiredCustomerAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<ExternalCustomer>(cancellationToken)
        ?? throw new HttpRequestException(
            "The external customers API returned an empty response body.");
}
