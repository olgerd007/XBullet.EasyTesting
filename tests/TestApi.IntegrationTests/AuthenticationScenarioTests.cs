using System.Net;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class AuthenticationScenarioTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public AuthenticationScenarioTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Azure_ad_profile_can_access_its_scheme_and_policy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsAzureAdUser(user => user
                .WithObjectId("user-object-42")
                .WithTenantId("tenant-42")
                .WithPreferredUsername("ada@example.test")
                .WithScope("orders.read"))
            .Build();

        using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);
        using var genericResponse = await client.GetAsync("/api/secure/me", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, genericResponse.StatusCode);
    }

    [Fact]
    public async Task Azure_ad_profile_is_rejected_by_api_key_scheme()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsAzureAdUser(user => user
                .WithTenantId("tenant-42")
                .WithScope("orders.read"))
            .Build();

        using var response = await client.GetAsync("/api/secure/api-key", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_key_profile_can_access_its_scheme_and_policy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsApiKey(apiKey => apiKey
                .WithKeyId("partner-key")
                .WithClientName("Fulfilment partner"))
            .Build();

        using var response = await client.GetAsync("/api/secure/api-key", cancellationToken);
        using var genericResponse = await client.GetAsync("/api/secure/me", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, genericResponse.StatusCode);
    }

    [Fact]
    public async Task Api_key_profile_is_rejected_by_azure_ad_scheme()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsApiKey(apiKey => apiKey.WithKeyId("partner-key"))
            .Build();

        using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Azure_ad_profile_without_required_claim_is_forbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsAzureAdUser(user => user
                .WithTenantId("tenant-42")
                .WithScope("orders.write"))
            .Build();

        using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
