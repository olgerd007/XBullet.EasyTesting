using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class EndToEndAuthenticationTests : IClassFixture<EndToEndAuthenticationFactory>
{
    private readonly EndToEndAuthenticationFactory _factory;

    public EndToEndAuthenticationTests(EndToEndAuthenticationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Locally_signed_jwt_is_validated_by_the_real_bearer_handler() =>
        Run(async (scope, cancellationToken) =>
        {
            using var result = await scope.Scenario()
                .AsJwt(token => token
                    .WithSubject("user-42")
                    .WithName("Ada")
                    .WithClaim("tid", "tenant-42")
                    .WithScope("orders.read")
                    .ExpiresAfter(TimeSpan.FromMinutes(1)))
                .Get("/api/secure/azure-ad")
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, result.Response.StatusCode);
        });

    [Fact]
    public Task Jwt_roles_are_evaluated_by_real_authorization() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsJwt(token => token.WithRole("Administrator"))
                .Build();

            using var response = await client.GetAsync("/api/secure/admin", cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        });

    [Theory]
    [InlineData("expired")]
    [InlineData("malformed")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("invalid-signature")]
    [InlineData("unknown-key")]
    [InlineData("unsigned")]
    [InlineData("not-yet-valid")]
    public Task Invalid_jwt_scenarios_are_rejected(string scenario) =>
        Run(async (scope, cancellationToken) =>
        {
            var scenarioBuilder = scope.Scenario();
            switch (scenario)
            {
                case "expired":
                    scenarioBuilder.AsExpiredJwt();
                    break;
                case "malformed":
                    scenarioBuilder.AsMalformedJwt();
                    break;
                case "wrong-audience":
                    scenarioBuilder.AsJwtWithWrongAudience();
                    break;
                case "wrong-issuer":
                    scenarioBuilder.AsJwtWithWrongIssuer();
                    break;
                case "invalid-signature":
                    scenarioBuilder.AsJwtWithInvalidSignature();
                    break;
                case "unknown-key":
                    scenarioBuilder.AsJwtWithUnknownKey();
                    break;
                case "unsigned":
                    scenarioBuilder.AsUnsignedJwt();
                    break;
                case "not-yet-valid":
                    scenarioBuilder.AsJwtNotYetValid();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown JWT scenario '{scenario}'.");
            }

            using var result = await scenarioBuilder
                .Get("/api/secure/azure-ad")
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, result.Response.StatusCode);
            scope.AuthenticationEvents.Should()
                .HaveValidationFailure(authenticationScheme: "AzureAd")
                .HaveChallenge(authenticationScheme: "AzureAd");
        });

    [Fact]
    public Task Jwks_rotation_is_refreshed_through_the_oidc_backchannel() =>
        Run(async (scope, cancellationToken) =>
        {
            using (var firstClient = scope.Client().AsJwt().Build())
            using (var firstResponse = await firstClient.GetAsync("/api/secure/admin", cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Forbidden, firstResponse.StatusCode);
            }

            var authority = scope.JwtAuthority("AzureAd");
            var previousKeyId = authority.CurrentKeyId;
            var currentKeyId = authority.RotateSigningKey();
            using var rotatedClient = scope.Client()
                .AsJwt(token => token.WithRole("Administrator"))
                .Build();
            using var rotatedResponse = await rotatedClient.GetAsync(
                "/api/secure/admin",
                cancellationToken);
            var rotatedStatusCode = rotatedResponse.StatusCode;

            // IdentityModel 8 refreshes signing-key metadata in the background by default,
            // so the request that detects the rotated key can receive a 401. Once the
            // refresh completes, the same token must succeed on the next request.
            if (rotatedStatusCode == HttpStatusCode.Unauthorized)
            {
                var refreshDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
                while (authority.JwksRequestCount < 2 && DateTimeOffset.UtcNow < refreshDeadline)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
                }

                using var retryResponse = await rotatedClient.GetAsync(
                    "/api/secure/admin",
                    cancellationToken);
                rotatedStatusCode = retryResponse.StatusCode;
            }

            Assert.NotEqual(previousKeyId, currentKeyId);
            Assert.Equal(HttpStatusCode.NoContent, rotatedStatusCode);
            Assert.True(authority.DiscoveryRequestCount >= 1);
            Assert.True(authority.JwksRequestCount >= 2);
        });

    [Fact]
    public Task Named_jwt_authority_can_be_selected_fluently() =>
        Run(async (scope, cancellationToken) =>
        {
            using var result = await scope.Scenario()
                .AsJwt("PartnerBearer", token => token.WithClaim("partner", "trusted"))
                .Get("/api/secure/partner")
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, result.Response.StatusCode);
            scope.AuthenticationEvents.Should()
                .HaveValidatedCredential(authenticationScheme: "PartnerBearer");

            using var client = scope.CreateAnonymousClient();
            using var discovery = await client.GetAsync(
                "/.well-known/partner/openid-configuration",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        });

    [Fact]
    public Task Authorization_forbid_event_can_be_asserted_fluently() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client().AsJwt().Build();

            using var response = await client.GetAsync("/api/secure/azure-ad", cancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            scope.AuthenticationEvents.Should()
                .HaveValidatedCredential(authenticationScheme: "AzureAd")
                .HaveForbidden(authenticationScheme: "AzureAd");
        });

    [Fact]
    public Task Saved_access_token_is_available_in_real_authentication_properties() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client().AsJwt().Build();

            using var response = await client.GetAsync(
                "/api/secure/authentication-details",
                cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(
                body.GetProperty("properties").GetProperty(".Token.access_token").GetString()!);
        });

    [Fact]
    public Task Client_certificate_is_validated_by_the_real_certificate_handler() =>
        Run(async (scope, cancellationToken) =>
        {
            using var result = await scope.Scenario()
                .WithClientCertificate(certificate => certificate
                    .WithSubject("CN=trusted-client"))
                .Get("/api/secure/certificate")
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, result.Response.StatusCode);
            scope.AuthenticationEvents.Should()
                .HaveValidatedCredential(authenticationScheme: "Certificate");
        });

    [Fact]
    public Task Authentication_diagnostics_do_not_capture_bearer_tokens() =>
        Run(async (scope, cancellationToken) =>
        {
            var token = scope.JwtAuthority("AzureAd").CreateInvalidSignatureToken();
            using var result = await scope.Scenario()
                .WithHeader("Authorization", $"Bearer {token}")
                .Get("/api/secure/azure-ad")
                .ExecuteAsync(cancellationToken);
            var diagnostics = JsonSerializer.Serialize(scope.AuthenticationEvents.Events);

            Assert.Equal(HttpStatusCode.Unauthorized, result.Response.StatusCode);
            Assert.DoesNotContain(token, diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", diagnostics, StringComparison.OrdinalIgnoreCase);
        });

    [Fact]
    public Task Oidc_discovery_and_jwks_are_exposed_by_the_test_host() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAnonymousClient();
            using var discoveryResponse = await client.GetAsync(
                "/.well-known/openid-configuration",
                cancellationToken);
            using var jwksResponse = await client.GetAsync(
                "/.well-known/jwks.json",
                cancellationToken);
            var discovery = await discoveryResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var jwks = await jwksResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
            Assert.Equal("https://identity.xbullet.test", discovery.GetProperty("issuer").GetString());
            Assert.Equal(
                "https://identity.xbullet.test/.well-known/jwks.json",
                discovery.GetProperty("jwks_uri").GetString());
            Assert.Equal(HttpStatusCode.OK, jwksResponse.StatusCode);
            Assert.Equal("RSA", jwks.GetProperty("keys")[0].GetProperty("kty").GetString());
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Real_api_key_can_be_injected_in_header_or_query(bool useHeader) =>
        Run(async (scope, cancellationToken) =>
        {
            var builder = scope.Scenario();
            if (useHeader)
            {
                builder.WithApiKeyHeader("integration-secret");
            }
            else
            {
                builder.WithApiKeyQuery("integration-secret");
            }

            using var result = await builder
                .Get("/api/secure/api-key")
                .ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, result.Response.StatusCode);
            scope.AuthenticationEvents.Should()
                .HaveValidatedCredential(authenticationScheme: "ApiKey");
        });

    [Fact]
    public Task Invalid_real_api_key_is_rejected() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .WithApiKeyHeader("wrong-secret")
                .Build();

            using var response = await client.GetAsync("/api/secure/api-key", cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            scope.AuthenticationEvents.Should()
                .HaveValidationFailure(authenticationScheme: "ApiKey")
                .HaveChallenge(authenticationScheme: "ApiKey");
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);
}
