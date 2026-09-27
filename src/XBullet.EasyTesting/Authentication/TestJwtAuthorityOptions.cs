namespace XBullet.EasyTesting.Authentication;

/// <summary>Configures the local JWT authority used by end-to-end authentication tests.</summary>
public sealed class TestJwtAuthorityOptions
{
    /// <summary>Gets or sets the default token issuer.</summary>
    /// <value>
    /// The non-empty issuer string used for metadata, token creation, and validation. The default is
    /// <c>https://xbullet.easytesting.test</c>.
    /// </value>
    public string Issuer { get; set; } = "https://xbullet.easytesting.test";

    /// <summary>Gets or sets the default token audience.</summary>
    /// <value>
    /// The non-empty audience used for token creation and validation. The default is
    /// <c>xbullet-easytesting</c>.
    /// </value>
    public string Audience { get; set; } = "xbullet-easytesting";

    /// <summary>Gets or sets the default lifetime of newly created tokens.</summary>
    /// <value>
    /// A positive duration measured from token build time. The default is five minutes. Directly
    /// assigned values are validated when the authority is registered or created.
    /// </value>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the validation clock skew.</summary>
    /// <value>
    /// A non-negative tolerance applied to lifetime validation. The default is
    /// <see cref="TimeSpan.Zero"/>. Directly assigned values are validated when the authority is
    /// registered or created.
    /// </value>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets the OIDC discovery endpoint path exposed by the test host.</summary>
    /// <value>
    /// A non-empty application path. The default is
    /// <c>/.well-known/openid-configuration</c>. A missing leading slash is added when the path is
    /// used.
    /// </value>
    public string DiscoveryPath { get; set; } = "/.well-known/openid-configuration";

    /// <summary>Gets or sets the JWKS endpoint path exposed by the test host.</summary>
    /// <value>
    /// A non-empty application path. The default is <c>/.well-known/jwks.json</c>. A missing leading
    /// slash is added when the path is used.
    /// </value>
    public string JwksPath { get; set; } = "/.well-known/jwks.json";

    /// <summary>Gets or sets whether this scheme becomes the host's default authentication scheme.</summary>
    /// <value>
    /// <see langword="true"/> to replace the host's default authentication scheme with this JWT
    /// scheme; <see langword="false"/> to preserve the existing default. The default is
    /// <see langword="true"/>.
    /// </value>
    public bool UseAsDefaultScheme { get; set; } = true;

    /// <summary>Gets or sets whether the validated bearer token is saved in authentication properties.</summary>
    /// <value>
    /// <see langword="true"/> to save a successfully validated bearer token in the authentication
    /// ticket; otherwise, <see langword="false"/>. The default is <see langword="false"/>.
    /// </value>
    public bool SaveToken { get; set; }

    /// <summary>Sets the default issuer.</summary>
    /// <param name="issuer">
    /// The non-empty issuer string. Trailing slash characters are removed; the remaining value is
    /// not otherwise normalized or validated as a URI.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="issuer"/> is empty or whitespace.</exception>
    public TestJwtAuthorityOptions WithIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        Issuer = issuer.TrimEnd('/');
        return this;
    }

    /// <summary>Sets the default audience.</summary>
    /// <param name="audience">The non-empty audience used for token creation and validation.</param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="audience"/> is empty or whitespace.</exception>
    public TestJwtAuthorityOptions WithAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        Audience = audience;
        return this;
    }

    /// <summary>Sets the default token lifetime.</summary>
    /// <param name="tokenLifetime">
    /// The positive duration added to token build time when no explicit expiration is configured.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tokenLifetime"/> is zero or negative.
    /// </exception>
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
    /// <param name="clockSkew">
    /// The non-negative tolerance applied to not-before and expiration validation. Use
    /// <see cref="TimeSpan.Zero"/> for exact boundary checks.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="clockSkew"/> is negative.
    /// </exception>
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
    /// <param name="discoveryPath">
    /// The non-empty application path. A leading slash is added when omitted.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="discoveryPath"/> is empty or whitespace.
    /// </exception>
    public TestJwtAuthorityOptions WithDiscoveryPath(string discoveryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryPath);
        DiscoveryPath = TestJwtAuthority.NormalizePath(discoveryPath);
        return this;
    }

    /// <summary>Sets the JWKS endpoint path exposed by the test host.</summary>
    /// <param name="jwksPath">The non-empty application path. A leading slash is added when omitted.</param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="jwksPath"/> is empty or whitespace.</exception>
    public TestJwtAuthorityOptions WithJwksPath(string jwksPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwksPath);
        JwksPath = TestJwtAuthority.NormalizePath(jwksPath);
        return this;
    }

    /// <summary>Prevents this JWT scheme from replacing the application's default scheme.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public TestJwtAuthorityOptions WithoutDefaultScheme()
    {
        UseAsDefaultScheme = false;
        return this;
    }

    /// <summary>Saves validated access tokens in the real handler's authentication properties.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public TestJwtAuthorityOptions SaveAccessToken()
    {
        SaveToken = true;
        return this;
    }
}
