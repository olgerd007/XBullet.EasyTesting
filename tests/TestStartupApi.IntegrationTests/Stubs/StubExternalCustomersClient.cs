using TestStartupApi.External;

namespace TestStartupApi.IntegrationTests.Stubs;

public sealed class StubExternalCustomersClient
    : StubServiceClientBase<ExternalCustomersOperation>, IExternalCustomersClient
{
    public StubExternalCustomersClient ReturnsList(params ExternalCustomer[] response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArrangeResponse<IReadOnlyList<ExternalCustomer>>(
            ExternalCustomersOperation.List,
            response.ToArray());
        return this;
    }

    public StubExternalCustomersClient ReturnsGet(
        string customerId,
        ExternalCustomer? response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArrangeResponse(ExternalCustomersOperation.Get, response, customerId);
        return this;
    }

    public StubExternalCustomersClient ReturnsCreate(ExternalCustomer response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArrangeResponse(ExternalCustomersOperation.Create, response);
        return this;
    }

    public StubExternalCustomersClient ReturnsUpdate(
        string customerId,
        ExternalCustomer? response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArrangeResponse(ExternalCustomersOperation.Update, response, customerId);
        return this;
    }

    public StubExternalCustomersClient ReturnsDelete(string customerId, bool response = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArrangeResponse(ExternalCustomersOperation.Delete, response, customerId);
        return this;
    }

    public Task<IReadOnlyList<ExternalCustomer>> ListAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync<IReadOnlyList<ExternalCustomer>>(
            ExternalCustomersOperation.List,
            cancellationToken: cancellationToken);

    public Task<ExternalCustomer?> GetAsync(
        string customerId,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ExternalCustomer?>(
            ExternalCustomersOperation.Get,
            customerId,
            cancellationToken: cancellationToken);

    public Task<ExternalCustomer> CreateAsync(
        ExternalCustomerInput customer,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ExternalCustomer>(
            ExternalCustomersOperation.Create,
            request: customer,
            cancellationToken: cancellationToken);

    public Task<ExternalCustomer?> UpdateAsync(
        string customerId,
        ExternalCustomerInput customer,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ExternalCustomer?>(
            ExternalCustomersOperation.Update,
            customerId,
            customer,
            cancellationToken);

    public Task<bool> DeleteAsync(
        string customerId,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>(
            ExternalCustomersOperation.Delete,
            customerId,
            cancellationToken: cancellationToken);
}

public enum ExternalCustomersOperation
{
    List,
    Get,
    Create,
    Update,
    Delete
}
