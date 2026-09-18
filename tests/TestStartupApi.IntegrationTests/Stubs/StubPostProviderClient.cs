using TestStartupApi.External;

namespace TestStartupApi.IntegrationTests.Stubs;

public sealed class StubPostProviderClient
    : StubServiceClientBase<PostProviderOperation>, IPostProviderClient
{
    public StubPostProviderClient ReturnsCreateShipment(
        string externalOrderId,
        PostShipmentResult response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalOrderId);
        ArgumentNullException.ThrowIfNull(response);
        ArrangeResponse(PostProviderOperation.CreateShipment, response, externalOrderId);
        return this;
    }

    public Task<PostShipmentResult> CreateShipmentAsync(
        PostShipmentRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<PostShipmentResult>(
            PostProviderOperation.CreateShipment,
            request.OrderId,
            request,
            cancellationToken);

    public StubPostProviderClient VerifyCreateShipmentCalled(
        string externalOrderId,
        int expectedCount = 1)
    {
        VerifyCalled(PostProviderOperation.CreateShipment, externalOrderId, expectedCount);
        return this;
    }
}

public enum PostProviderOperation
{
    CreateShipment
}
