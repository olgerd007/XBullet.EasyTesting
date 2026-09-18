namespace TestStartupApi.External;

public interface IExternalCustomersClient
{
    Task<IReadOnlyList<ExternalCustomer>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<ExternalCustomer?> GetAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<ExternalCustomer> CreateAsync(
        ExternalCustomerInput customer,
        CancellationToken cancellationToken = default);

    Task<ExternalCustomer?> UpdateAsync(
        string customerId,
        ExternalCustomerInput customer,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string customerId,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalCustomer(
    string Id,
    string DisplayName,
    string Email);

public sealed record ExternalCustomerInput(
    string DisplayName,
    string Email);
