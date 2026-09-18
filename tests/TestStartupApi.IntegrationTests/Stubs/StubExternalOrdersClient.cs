using TestStartupApi.External;

namespace TestStartupApi.IntegrationTests.Stubs;

public sealed class StubExternalOrdersClient
    : StubServiceClientBase<ExternalOrdersOperation>, IExternalOrdersClient
{
    public StubExternalOrdersClient Returns(string externalId, ExternalOrder? response) =>
        ReturnsGet(externalId, response);

    public StubExternalOrdersClient ReturnsList(params ExternalOrder[] response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArrangeResponse<IReadOnlyList<ExternalOrder>>(
            ExternalOrdersOperation.List,
            response.ToArray());
        return this;
    }

    public StubExternalOrdersClient ReturnsGet(string externalId, ExternalOrder? response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArrangeResponse(ExternalOrdersOperation.Get, response, externalId);
        return this;
    }

    public StubExternalOrdersClient ReturnsCreate(ExternalOrder response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArrangeResponse(ExternalOrdersOperation.Create, response);
        return this;
    }

    public StubExternalOrdersClient ReturnsUpdate(string externalId, ExternalOrder? response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArrangeResponse(ExternalOrdersOperation.Update, response, externalId);
        return this;
    }

    public StubExternalOrdersClient ReturnsDelete(string externalId, bool response = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArrangeResponse(ExternalOrdersOperation.Delete, response, externalId);
        return this;
    }

    public Task<IReadOnlyList<ExternalOrder>> ListAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync<IReadOnlyList<ExternalOrder>>(
            ExternalOrdersOperation.List,
            cancellationToken: cancellationToken);

    public Task<ExternalOrder?> GetAsync(
        string externalId,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ExternalOrder?>(
            ExternalOrdersOperation.Get,
            externalId,
            cancellationToken: cancellationToken);

    public Task<ExternalOrder> CreateAsync(
        ExternalOrderInput order,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ExternalOrder>(
            ExternalOrdersOperation.Create,
            request: order,
            cancellationToken: cancellationToken);

    public Task<ExternalOrder?> UpdateAsync(
        string externalId,
        ExternalOrderInput order,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ExternalOrder?>(
            ExternalOrdersOperation.Update,
            externalId,
            order,
            cancellationToken);

    public Task<bool> DeleteAsync(
        string externalId,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<bool>(
            ExternalOrdersOperation.Delete,
            externalId,
            cancellationToken: cancellationToken);

    public StubExternalOrdersClient VerifyGetCalled(
        string externalId,
        int expectedCount = 1)
    {
        VerifyCalled(ExternalOrdersOperation.Get, externalId, expectedCount);
        return this;
    }
}

public enum ExternalOrdersOperation
{
    List,
    Get,
    Create,
    Update,
    Delete
}
