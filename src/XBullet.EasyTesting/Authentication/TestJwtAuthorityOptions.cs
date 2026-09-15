namespace XBullet.EasyTesting.Authentication;

/// <summary>Configures the local JWT authority used by end-to-end authentication tests.</summary>
public sealed class TestJwtAuthorityOptions
{
    /// <summary>Gets or sets the default token issuer.</summary>
    public string Issuer { get; set; } = "https://xbullet.easytesting.test";

    /// <summary>Gets or sets the default token audience.</summary>
    public string Audience { get; set; } = "xbullet-easytesting";

    /// <summary>Gets or sets the default lifetime of newly created tokens.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the validation clock skew.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets the OIDC discovery endpoint path exposed by the test host.</summary>
    public string DiscoveryPath { get; set; } = "/.well-known/openid-configuration";

    /// <summary>Gets or sets the JWKS endpoint path exposed by the test host.</summary>
    public string JwksPath { get; set; } = "/.well-known/jwks.json";

    /// <summary>Gets or sets whether this scheme becomes the host's default authentication scheme.</summary>
    public bool UseAsDefaultScheme { get; set; } = true;

    /// <summary>Gets or sets whether the validated bearer token is saved in authentication properties.</summary>
    public bool SaveToken { get; set; }

    /// <summary>Sets the default issuer.</summary>
    public TestJwtAuthorityOptions WithIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        Issuer = issuer.TrimEnd('/');
        return this;
    }

    /// <summary>Sets the default audience.</summary>
    public TestJwtAuthorityOptions WithAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        Audience = audience;
        return this;
    }

    /// <summary>Sets the default token lifetime.</summary>
    public TestJwtAuthorityOptions WithTokenLifetime(TimeSpan tokenLifetime)
    {
        if (tokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenLifetime));
        }

        TokenLifetime = tokenLifetime;
        return this;
    }

    /// <summary>Sets the validation clock skew.</summary>
    public TestJwtAuthorityOptions WithClockSkew(TimeSpan clockSkew)
    {
        if (clockSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(clockSkew));
        }

        ClockSkew = clockSkew;
        return this;
    }

    /// <summary>Sets the discovery endpoint path exposed by the test host.</summary>
    public TestJwtAuthorityOptions WithDiscoveryPath(string discoveryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryPath);
        DiscoveryPath = TestJwtAuthority.NormalizePath(discoveryPath);
        return this;
    }

    /// <summary>Sets the JWKS endpoint path exposed by the test host.</summary>
    public TestJwtAuthorityOptions WithJwksPath(string jwksPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwksPath);
        JwksPath = TestJwtAuthority.NormalizePath(jwksPath);
        return this;
    }

    /// <summary>Prevents this JWT scheme from replacing the application's default scheme.</summary>
    public TestJwtAuthorityOptions WithoutDefaultScheme()
    {
        UseAsDefaultScheme = false;
        return this;
    }

    /// <summary>Saves validated access tokens in the real handler's authentication properties.</summary>
    public TestJwtAuthorityOptions SaveAccessToken()
    {
        SaveToken = true;
        return this;
    }
}
