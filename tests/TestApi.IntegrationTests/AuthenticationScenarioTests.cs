using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XBullet.EasyTesting.Hosting;
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
    public Task Azure_ad_profile_can_access_its_scheme_and_policy() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
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
        });

    [Fact]
    public Task Azure_ad_profile_is_rejected_by_api_key_scheme() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsAzureAdUser(user => user
                    .WithTenantId("tenant-42")
                    .WithScope("orders.read"))
                .Build();

            using var response = await client.GetAsync("/api/secure/api-key", cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

    [Fact]
    public Task Api_key_profile_can_access_its_scheme_and_policy() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsApiKey(apiKey => apiKey
                    .WithKeyId("partner-key")
                    .WithClientName("Fulfilment partner"))
                .Build();

            using var response = await client.GetAsync("/api/secure/api-key", cancellationToken);
            using var genericResponse = await client.GetAsync("/api/secure/me", cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, genericResponse.StatusCode);
        });

    [Fact]
    public Task Api_key_profile_is_rejected_by_azure_ad_scheme() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsApiKey(apiKey => apiKey.WithKeyId("partner-key"))
                .Build();

            using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

    [Fact]
    public Task Azure_ad_profile_without_required_claim_is_forbidden() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsAzureAdUser(user => user
                    .WithTenantId("tenant-42")
                    .WithScope("orders.write"))
                .Build();

            using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        });

    [Fact]
    public Task Simulated_principal_supports_multiple_identities_and_authentication_properties() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsUser(user => user
                    .WithName("Primary identity")
                    .WithIdentity(identity => identity
                        .WithAuthenticationType("DelegatedIdentity")
                        .WithClaim(System.Security.Claims.ClaimTypes.Name, "Secondary identity")
                        .WithClaim("tenant", "secondary"))
                    .WithAuthenticationProperty("refresh_token", "test-refresh-token"))
                .Build();

            using var response = await client.GetAsync(
                "/api/secure/authentication-details",
                cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, body.GetProperty("identities").GetArrayLength());
            Assert.Equal(
                "DelegatedIdentity",
                body.GetProperty("identities")[1].GetProperty("authenticationType").GetString());
            Assert.Equal(
                "test-refresh-token",
                body.GetProperty("properties").GetProperty("refresh_token").GetString());
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);
}
