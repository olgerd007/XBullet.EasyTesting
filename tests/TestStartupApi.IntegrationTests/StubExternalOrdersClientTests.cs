using TestStartupApi.External;
using TestStartupApi.IntegrationTests.Stubs;
using Xunit;

namespace TestStartupApi.IntegrationTests;

public sealed class StubExternalOrdersClientTests
{
    [Fact]
    public async Task Crud_calls_can_be_arranged_and_verified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existing = new ExternalOrder("order-1", "customer-1", "Existing", 10m);
        var created = new ExternalOrder("order-2", "customer-2", "Created", 20m);
        var updated = new ExternalOrder("order-1", "customer-1", "Updated", 30m);
        var createInput = new ExternalOrderInput("customer-2", "Created", 20m);
        var updateInput = new ExternalOrderInput("customer-1", "Updated", 30m);
        var stub = new StubExternalOrdersClient()
            .ReturnsList(existing)
            .ReturnsGet(existing.Id, existing)
            .ReturnsCreate(created)
            .ReturnsUpdate(existing.Id, updated)
            .ReturnsDelete(existing.Id);

        var listed = await stub.ListAsync(cancellationToken);
        var fetched = await stub.GetAsync(existing.Id, cancellationToken);
        var createResult = await stub.CreateAsync(createInput, cancellationToken);
        var updateResult = await stub.UpdateAsync(existing.Id, updateInput, cancellationToken);
        var deleted = await stub.DeleteAsync(existing.Id, cancellationToken);

        Assert.Equal(existing, Assert.Single(listed));
        Assert.Equal(existing, fetched);
        Assert.Equal(created, createResult);
        Assert.Equal(updated, updateResult);
        Assert.True(deleted);
        stub.VerifyCalled(ExternalOrdersOperation.List)
            .VerifyCalled(ExternalOrdersOperation.Get, existing.Id)
            .VerifyCalled(ExternalOrdersOperation.Create)
            .VerifyCalled(ExternalOrdersOperation.Update, existing.Id)
            .VerifyCalled(ExternalOrdersOperation.Delete, existing.Id);
        Assert.Equal(
            createInput,
            Assert.IsType<ExternalOrderInput>(stub.Calls
                .Single(call => call.Operation == ExternalOrdersOperation.Create)
                .Request));
        Assert.Equal(
            updateInput,
            Assert.IsType<ExternalOrderInput>(stub.Calls
                .Single(call => call.Operation == ExternalOrdersOperation.Update)
                .Request));
    }
}
