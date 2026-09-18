using TestStartupApi.External;
using TestStartupApi.IntegrationTests.Stubs;
using Xunit;

namespace TestStartupApi.IntegrationTests;

public sealed class StubExternalCustomersClientTests
{
    [Fact]
    public async Task Customer_stub_reuses_generic_arrangement_and_verification_behavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var customer = new ExternalCustomer(
            "customer-7",
            "Ada Lovelace",
            "ada@example.test");
        var input = new ExternalCustomerInput(customer.DisplayName, customer.Email);
        var stub = new StubExternalCustomersClient()
            .ReturnsList(customer)
            .ReturnsGet(customer.Id, customer)
            .ReturnsCreate(customer)
            .ReturnsUpdate(customer.Id, customer)
            .ReturnsDelete(customer.Id);

        Assert.Equal(customer, Assert.Single(await stub.ListAsync(cancellationToken)));
        Assert.Equal(customer, await stub.GetAsync(customer.Id, cancellationToken));
        Assert.Equal(customer, await stub.CreateAsync(input, cancellationToken));
        Assert.Equal(customer, await stub.UpdateAsync(customer.Id, input, cancellationToken));
        Assert.True(await stub.DeleteAsync(customer.Id, cancellationToken));
        stub.VerifyCalled(ExternalCustomersOperation.List)
            .VerifyCalled(ExternalCustomersOperation.Get, customer.Id)
            .VerifyCalled(ExternalCustomersOperation.Create)
            .VerifyCalled(ExternalCustomersOperation.Update, customer.Id)
            .VerifyCalled(ExternalCustomersOperation.Delete, customer.Id);
    }
}
