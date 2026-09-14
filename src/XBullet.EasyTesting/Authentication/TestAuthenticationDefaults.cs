namespace XBullet.EasyTesting.Authentication;

/// <summary>Constants used by the test authentication handler.</summary>
public static class TestAuthenticationDefaults
{
    /// <summary>The authentication scheme installed in the test server.</summary>
    public const string AuthenticationScheme = "IntegrationTest";

    /// <summary>The internal request header carrying a serialized test identity.</summary>
    public const string UserHeaderName = "X-Integration-Test-User";

    /// <summary>Claim identifying the authentication profile represented by a test principal.</summary>
    public const string AuthenticationMethodClaim = "integration_test_authentication_method";

    /// <summary>Authentication-method value used by Azure AD test principals.</summary>
    public const string AzureAdAuthenticationMethod = "AzureAd";

    /// <summary>Authentication-method value used by API-key test principals.</summary>
    public const string ApiKeyAuthenticationMethod = "ApiKey";

    /// <summary>Authentication type exposed by Azure AD test identities.</summary>
    public const string AzureAdAuthenticationType = "AzureAd";

    /// <summary>Authentication type exposed by API-key test identities.</summary>
    public const string ApiKeyAuthenticationType = "ApiKey";
}
