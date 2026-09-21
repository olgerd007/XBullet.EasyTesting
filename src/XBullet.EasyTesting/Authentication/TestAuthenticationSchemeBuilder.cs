namespace XBullet.EasyTesting.Authentication;

/// <summary>Maps test authentication profiles to scheme names used by the application.</summary>
public sealed class TestAuthenticationSchemeBuilder
{
    private readonly HashSet<string> _additionalSchemes = new(StringComparer.Ordinal);
    private readonly List<EndToEndJwtRegistration> _endToEndJwtRegistrations = [];
    private readonly List<EndToEndClientCertificateRegistration> _endToEndCertificateRegistrations = [];

    internal TestAuthenticationEventRecorder EventRecorder { get; } = new();

    internal bool PreserveApplicationDefaultScheme { get; private set; }

    internal string? HybridDefaultTestScheme { get; private set; }

    internal string AzureAdScheme { get; private set; } = TestAuthenticationDefaults.AuthenticationScheme;

    internal string ApiKeyScheme { get; private set; } = TestAuthenticationDefaults.AuthenticationScheme;

    internal string FederationScheme { get; private set; } = TestAuthenticationDefaults.AuthenticationScheme;

    internal IReadOnlyCollection<string> AdditionalSchemes => _additionalSchemes;

    internal IReadOnlyCollection<EndToEndJwtRegistration> EndToEndJwtRegistrations =>
        _endToEndJwtRegistrations;

    internal TestApiKeyInjectionOptions? EndToEndApiKey { get; private set; }

    internal IReadOnlyCollection<EndToEndClientCertificateRegistration>
        EndToEndCertificateRegistrations => _endToEndCertificateRegistrations;

    internal string? DefaultEndToEndScheme =>
        _endToEndJwtRegistrations.FirstOrDefault(registration =>
            registration.Authority.Options.UseAsDefaultScheme)?.AuthenticationScheme
        ?? (EndToEndApiKey?.UseAsDefaultScheme == true
            ? EndToEndApiKey.AuthenticationScheme
            : _endToEndCertificateRegistrations.FirstOrDefault(registration =>
                registration.Options.UseAsDefaultScheme)?.AuthenticationScheme);

    internal TestJwtAuthority GetJwtAuthority(string? authenticationScheme = null)
    {
        var registration = authenticationScheme is null
            ? _endToEndJwtRegistrations.FirstOrDefault()
            : _endToEndJwtRegistrations.FirstOrDefault(candidate => string.Equals(
                candidate.AuthenticationScheme,
                authenticationScheme,
                StringComparison.Ordinal));
        return registration?.Authority
            ?? throw new InvalidOperationException(authenticationScheme is null
                ? "End-to-end JWT authentication is not configured."
                : $"End-to-end JWT authentication is not configured for scheme '{authenticationScheme}'.");
    }

    /// <summary>Maps Azure AD test identities to an application scheme such as <c>Bearer</c>.</summary>
    public TestAuthenticationSchemeBuilder MapAzureAd(string authenticationScheme)
    {
        AzureAdScheme = AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>Maps API-key test identities to the application's API-key authentication scheme.</summary>
    public TestAuthenticationSchemeBuilder MapApiKey(string authenticationScheme)
    {
        ApiKeyScheme = AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>Maps Federation test identities to the application's authentication scheme.</summary>
    public TestAuthenticationSchemeBuilder MapFederation(string authenticationScheme)
    {
        FederationScheme = AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>Adds an application-specific authentication scheme handled by test identities.</summary>
    public TestAuthenticationSchemeBuilder MapScheme(string authenticationScheme)
    {
        AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>
    /// Adds a dedicated simulated-authentication scheme without changing an application scheme.
    /// Use this together with <see cref="PreserveDefaultAuthenticationScheme"/> when simulated
    /// identities and the application's real authentication handlers must coexist.
    /// </summary>
    public TestAuthenticationSchemeBuilder MapTestAuthentication(string authenticationScheme)
    {
        AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>
    /// Keeps the default authentication, challenge, and forbid schemes selected by the application.
    /// Simulated identities remain available through mapped test schemes.
    /// </summary>
    public TestAuthenticationSchemeBuilder PreserveDefaultAuthenticationScheme()
    {
        PreserveApplicationDefaultScheme = true;
        return this;
    }

    /// <summary>
    /// Uses a policy scheme that authenticates test-identity headers with the supplied simulated
    /// scheme and otherwise forwards to the application's original default authentication scheme.
    /// The application's original challenge and forbid schemes remain unchanged.
    /// </summary>
    public TestAuthenticationSchemeBuilder UseHybridDefaultAuthentication(
        string testAuthenticationScheme)
    {
        PreserveApplicationDefaultScheme = true;
        HybridDefaultTestScheme = AddScheme(testAuthenticationScheme);
        return this;
    }

    /// <summary>
    /// Uses the application's real JWT bearer scheme with tokens signed by a local test authority.
    /// The application must register the named scheme with <c>AddJwtBearer</c>.
    /// </summary>
    public TestAuthenticationSchemeBuilder UseEndToEndJwt(
        string authenticationScheme,
        Action<TestJwtAuthorityOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        if (_endToEndJwtRegistrations.Any(registration => string.Equals(
            registration.AuthenticationScheme,
            authenticationScheme,
            StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"End-to-end JWT authentication is already configured for scheme '{authenticationScheme}'.");
        }

        var options = new TestJwtAuthorityOptions();
        configure?.Invoke(options);
        ValidateJwtOptions(options);
        if (_endToEndJwtRegistrations.Any(registration =>
            string.Equals(
                registration.Authority.Options.DiscoveryPath,
                options.DiscoveryPath,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                registration.Authority.Options.JwksPath,
                options.JwksPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Each end-to-end JWT authority must use unique discovery and JWKS endpoint paths.");
        }

        _additionalSchemes.Remove(authenticationScheme);
        AzureAdScheme = authenticationScheme;
        _endToEndJwtRegistrations.Add(
            new EndToEndJwtRegistration(authenticationScheme, new TestJwtAuthority(options)));
        return this;
    }

    /// <summary>
    /// Uses the application's real API-key authentication scheme and configures client-side key injection.
    /// </summary>
    public TestAuthenticationSchemeBuilder UseEndToEndApiKey(
        string authenticationScheme,
        Action<TestApiKeyInjectionOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        var options = new TestApiKeyInjectionOptions
        {
            AuthenticationScheme = authenticationScheme
        };
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HeaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.QueryParameterName);
        _additionalSchemes.Remove(authenticationScheme);
        ApiKeyScheme = authenticationScheme;
        EndToEndApiKey = options;
        return this;
    }

    /// <summary>
    /// Uses the application's real certificate authentication scheme and transports a certificate to TestServer.
    /// The application must register the named scheme with <c>AddCertificate</c>.
    /// </summary>
    public TestAuthenticationSchemeBuilder UseEndToEndClientCertificate(
        string authenticationScheme,
        Action<TestClientCertificateOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        if (_endToEndCertificateRegistrations.Any(registration => string.Equals(
            registration.AuthenticationScheme,
            authenticationScheme,
            StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"End-to-end client-certificate authentication is already configured for scheme " +
                $"'{authenticationScheme}'.");
        }

        var options = new TestClientCertificateOptions();
        configure?.Invoke(options);
        _additionalSchemes.Remove(authenticationScheme);
        _endToEndCertificateRegistrations.Add(
            new EndToEndClientCertificateRegistration(authenticationScheme, options));
        return this;
    }

    internal EndToEndClientCertificateRegistration GetClientCertificateRegistration(
        string? authenticationScheme = null)
    {
        var registration = authenticationScheme is null
            ? _endToEndCertificateRegistrations.FirstOrDefault()
            : _endToEndCertificateRegistrations.FirstOrDefault(candidate => string.Equals(
                candidate.AuthenticationScheme,
                authenticationScheme,
                StringComparison.Ordinal));
        return registration
            ?? throw new InvalidOperationException(authenticationScheme is null
                ? "End-to-end client-certificate authentication is not configured."
                : $"End-to-end client-certificate authentication is not configured for scheme " +
                  $"'{authenticationScheme}'.");
    }

    private string AddScheme(string authenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        if (!string.Equals(
                authenticationScheme,
                TestAuthenticationDefaults.AuthenticationScheme,
                StringComparison.Ordinal))
        {
            _additionalSchemes.Add(authenticationScheme);
        }

        return authenticationScheme;
    }

    private static void ValidateJwtOptions(TestJwtAuthorityOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DiscoveryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.JwksPath);
        if (options.TokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.TokenLifetime));
        }

        if (options.ClockSkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.ClockSkew));
        }
    }

    internal sealed record EndToEndJwtRegistration(
        string AuthenticationScheme,
        TestJwtAuthority Authority);

    internal sealed record EndToEndClientCertificateRegistration(
        string AuthenticationScheme,
        TestClientCertificateOptions Options);
}
