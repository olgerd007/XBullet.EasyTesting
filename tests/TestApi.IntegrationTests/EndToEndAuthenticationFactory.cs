using XBullet.EasyTesting.Authentication;

namespace TestApi.IntegrationTests;

public sealed class EndToEndAuthenticationFactory : TestApiFactory
{
    protected override void ConfigureTestAuthentication(TestAuthenticationSchemeBuilder authentication)
    {
        authentication
            .UseEndToEndJwt("AzureAd", authority => authority
                .WithIssuer("https://identity.xbullet.test")
                .WithAudience("test-api")
                .WithTokenLifetime(TimeSpan.FromMinutes(2))
                .WithClockSkew(TimeSpan.Zero)
                .SaveAccessToken())
            .UseEndToEndJwt("PartnerBearer", authority => authority
                .WithIssuer("https://partner.xbullet.test")
                .WithAudience("test-api-partner")
                .WithDiscoveryPath("/.well-known/partner/openid-configuration")
                .WithJwksPath("/.well-known/partner/jwks.json")
                .WithoutDefaultScheme())
            .UseEndToEndApiKey("ApiKey", apiKey => apiKey
                .WithHeaderName("X-Api-Key")
                .WithQueryParameterName("api_key"))
            .UseEndToEndClientCertificate("Certificate");
    }
}
