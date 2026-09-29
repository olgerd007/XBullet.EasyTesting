using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using XBullet.EasyTesting.Authentication;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Fluently configures a client created by an authenticated application factory.</summary>
/// <remarks>
/// This mutable builder is not thread-safe. Configure and build a client from one execution flow.
/// Authentication-selection methods replace only the simulated identity or bearer token unless
/// their documentation states otherwise; explicitly configured headers and handlers remain.
/// </remarks>
/// <typeparam name="TEntryPoint">
/// The application entry-point type used by the factory that creates the client.
/// </typeparam>
public sealed class TestClientBuilder<TEntryPoint>
    where TEntryPoint : class
{
    private readonly Func<WebApplicationFactoryClientOptions, IReadOnlyCollection<DelegatingHandler>, HttpClient>
        _createClient;
    private readonly TestAuthenticationSchemeBuilder _authentication;
    private readonly string _azureAdAuthenticationScheme;
    private readonly string _apiKeyAuthenticationScheme;
    private readonly string _federationAuthenticationScheme;
    private readonly WebApplicationFactoryClientOptions _options = new();
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DelegatingHandler> _handlers = [];
    private TestUser? _user;
    private string? _bearerToken;

    internal TestClientBuilder(AuthenticatedWebApplicationFactory<TEntryPoint> factory)
        : this(
            (options, handlers) => TestHttpClientFactory.Create(factory, options, handlers),
            factory.GetAuthenticationConfigurationForClient())
    {
    }

    internal TestClientBuilder(
        Func<WebApplicationFactoryClientOptions, IReadOnlyCollection<DelegatingHandler>, HttpClient> createClient,
        TestAuthenticationSchemeBuilder authentication)
    {
        _createClient = createClient;
        _authentication = authentication;
        _azureAdAuthenticationScheme = authentication.AzureAdScheme;
        _apiKeyAuthenticationScheme = authentication.ApiKeyScheme;
        _federationAuthenticationScheme = authentication.FederationScheme;
    }

    /// <summary>Configures the client to send the supplied test identity.</summary>
    /// <param name="user">
    /// The non-null simulated identity to serialize into the test-authentication request header.
    /// The builder retains the immutable value but does not own it. This selection replaces any
    /// previously configured bearer token.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsUser(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _user = user;
        _bearerToken = null;
        return this;
    }

    /// <summary>Builds and configures the test identity used by this client.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new test-user builder. It is not retained or
    /// invoked concurrently. The resulting identity replaces any configured bearer token.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsUser(Action<TestUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = TestUser.CreateBuilder();
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds a test identity that targets a named simulated scheme.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new test-user builder initialized for
    /// <paramref name="authenticationScheme"/>. It is not retained or invoked concurrently.
    /// </param>
    /// <param name="authenticationScheme">
    /// The non-empty application authentication-scheme name written to the transported identity.
    /// The application must have a corresponding simulated scheme registration.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsUser(
        Action<TestUserBuilder> configure,
        string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        var builder = TestUser.CreateBuilder()
            .WithAuthenticationScheme(authenticationScheme)
            .WithAuthenticationType(authenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds an Azure AD-shaped identity for this client.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new Azure AD user builder targeting the
    /// configured Azure AD simulated scheme. It is not retained or invoked concurrently.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsAzureAdUser(Action<TestAzureAdUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestAzureAdUserBuilder(_azureAdAuthenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds an API-key-shaped identity for this client.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new API-key identity builder targeting the
    /// configured simulated API-key scheme. It configures claims, not a real API-key secret.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsApiKey(Action<TestApiKeyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestApiKeyBuilder(_apiKeyAuthenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds a Federation identity with an additional API-user identity.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new Federation user builder targeting the
    /// configured Federation scheme. It is not retained or invoked concurrently.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsFederatedUser(
        Action<TestFederatedUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestFederatedUserBuilder(_federationAuthenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Creates and sends a locally signed bearer token through the application's real JWT handler.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously before the token is signed. When
    /// <see langword="null"/>, the authority's default valid token is used. The callback runs once
    /// and is not retained.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwt(Action<TestJwtBuilder>? configure = null) =>
        AsJwtCore(authenticationScheme: null, configure);

    /// <summary>Creates a token for a named local authority and sends it through the real JWT handler.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty registered JWT authentication scheme whose local authority signs the token.
    /// </param>
    /// <param name="configure">
    /// Configures token claims synchronously before signing. When <see langword="null"/>, the
    /// selected authority's default valid token is used. The callback runs once and is not retained.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwt(
        string authenticationScheme,
        Action<TestJwtBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        return AsJwtCore(authenticationScheme, configure);
    }

    /// <summary>Sends an expired locally signed bearer token.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously after the expired lifetime is applied and before
    /// signing. When <see langword="null"/>, the expired defaults remain unchanged. The callback
    /// can replace the configured lifetime.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsExpiredJwt(Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateExpiredToken(configure));

    /// <summary>Sends a locally signed bearer token with an invalid audience.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously after the invalid audience is applied and before
    /// signing. When <see langword="null"/>, the invalid audience remains unchanged. The callback
    /// can replace the configured audience.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwtWithWrongAudience(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateWrongAudienceToken(configure));

    /// <summary>Sends a locally signed bearer token with an invalid issuer.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously after the invalid issuer is applied and before signing.
    /// When <see langword="null"/>, the invalid issuer remains unchanged. The callback can replace
    /// the configured issuer.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwtWithWrongIssuer(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateWrongIssuerToken(configure));

    /// <summary>Sends a deliberately malformed bearer token.</summary>
    /// <param name="value">
    /// The non-empty raw bearer-token value. When omitted, <c>not-a-valid-jwt</c> is used.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsMalformedJwt(string value = "not-a-valid-jwt") =>
        WithBearerToken(value);

    /// <summary>Sends a token whose signature is invalid for a known key identifier.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously before signing with an untrusted key whose identifier
    /// matches the current trusted key. When <see langword="null"/>, default claims are used. The
    /// callback cannot replace the untrusted signing key.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwtWithInvalidSignature(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateInvalidSignatureToken(configure));

    /// <summary>Sends a token whose key identifier is absent from JWKS.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously before signing with an untrusted key whose identifier
    /// is absent from JWKS. When <see langword="null"/>, default claims are used. The callback cannot
    /// replace the untrusted signing key.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwtWithUnknownKey(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateUnknownKeyToken(configure));

    /// <summary>Sends an unsigned JWT.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously before serialization. When <see langword="null"/>, the
    /// authority's default claims are used. No signing key is applied.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsUnsignedJwt(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateUnsignedToken(configure));

    /// <summary>Sends a token whose not-before time is in the future.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously after the future lifetime is applied and before
    /// signing. When <see langword="null"/>, the future not-before time remains unchanged. The
    /// callback can replace the configured lifetime.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> AsJwtNotYetValid(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateFutureNotBeforeToken(configure));

    /// <summary>Injects a real API key into every request header.</summary>
    /// <param name="value">
    /// The non-empty API-key secret sent without validation. The caller is responsible for using
    /// test-only credentials and preventing the value from appearing in logs or diagnostics.
    /// </param>
    /// <param name="headerName">
    /// The header name to use. When <see langword="null"/>, the name configured for end-to-end
    /// API-key authentication is used.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithApiKeyHeader(string value, string? headerName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var options = GetApiKeyOptions();
        return WithHeader(headerName ?? options.HeaderName, value);
    }

    /// <summary>Injects a real API key into every request query string.</summary>
    /// <param name="value">
    /// The non-empty API-key secret appended to every request without validation. Query strings can
    /// be logged by applications and infrastructure, so use test-only credentials.
    /// </param>
    /// <param name="parameterName">
    /// The query-parameter name to use. When <see langword="null"/>, the name configured for
    /// end-to-end API-key authentication is used.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithApiKeyQuery(string value, string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var options = GetApiKeyOptions();
        _handlers.Add(new ApiKeyQueryInjectionHandler(
            parameterName ?? options.QueryParameterName,
            value));
        return this;
    }

    /// <summary>Creates and transports a self-signed client certificate to the real certificate handler.</summary>
    /// <param name="configure">
    /// Configures a new self-signed certificate synchronously before it is exported. When
    /// <see langword="null"/>, certificate-builder defaults are used. The callback runs once and is
    /// not retained.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        Action<TestClientCertificateBuilder>? configure = null) =>
        WithClientCertificateCore(authenticationScheme: null, configure);

    /// <summary>Creates a client certificate for a named certificate authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty registered certificate authentication scheme that must accept the transported
    /// certificate.
    /// </param>
    /// <param name="configure">
    /// Configures a new self-signed certificate synchronously before it is exported. When
    /// <see langword="null"/>, certificate-builder defaults are used. The callback runs once and is
    /// not retained.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        Action<TestClientCertificateBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        return WithClientCertificateCore(authenticationScheme, configure);
    }

    /// <summary>Transports an existing client certificate to the real certificate handler.</summary>
    /// <param name="certificate">
    /// The certificate exported immediately into the internal transport header. The builder does
    /// not retain, own, or dispose the supplied certificate.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _ = _authentication.GetClientCertificateRegistration();
        return SetClientCertificate(certificate);
    }

    /// <summary>Transports an existing client certificate for a named authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty registered certificate authentication scheme that must accept the transported
    /// certificate.
    /// </param>
    /// <param name="certificate">
    /// The certificate exported immediately into the internal transport header. The builder does
    /// not retain, own, or dispose the supplied certificate.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        ArgumentNullException.ThrowIfNull(certificate);
        _ = _authentication.GetClientCertificateRegistration(authenticationScheme);
        return SetClientCertificate(certificate);
    }

    /// <summary>Configures the client to send no test identity.</summary>
    /// <returns>
    /// This builder. Any previously selected simulated identity or bearer token is removed; other
    /// configured credentials and headers are unchanged.
    /// </returns>
    public TestClientBuilder<TEntryPoint> AsAnonymous()
    {
        _user = null;
        _bearerToken = null;
        return this;
    }

    /// <summary>Sets the base address used by relative requests.</summary>
    /// <param name="baseAddress">
    /// The non-null base URI passed to the client factory. The builder retains the immutable URI but
    /// does not own it.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _options.BaseAddress = baseAddress;
        return this;
    }

    /// <summary>Prevents automatic HTTP redirect handling.</summary>
    /// <returns>This builder with automatic redirects disabled for the created client.</returns>
    public TestClientBuilder<TEntryPoint> WithoutRedirects()
    {
        _options.AllowAutoRedirect = false;
        return this;
    }

    /// <summary>Prevents automatic cookie persistence.</summary>
    /// <returns>This builder with automatic cookie handling disabled for the created client.</returns>
    public TestClientBuilder<TEntryPoint> WithoutCookies()
    {
        _options.HandleCookies = false;
        return this;
    }

    /// <summary>Adds or replaces a default request header.</summary>
    /// <param name="name">
    /// The non-empty header name. Matching existing configured names is case-insensitive.
    /// </param>
    /// <param name="value">
    /// The non-null header value. HTTP header validation occurs when the client is built; an empty
    /// value is passed through to that validation.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = value;
        return this;
    }

    /// <summary>Adds a delegating handler to the test client's HTTP pipeline.</summary>
    /// <param name="handler">
    /// The handler added after built-in redirect and cookie handlers, in registration order. Its
    /// <see cref="DelegatingHandler.InnerHandler"/> must be <see langword="null"/>. Ownership
    /// transfers to the client created by <see cref="Build"/>, which disposes it with the pipeline.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithHandler(DelegatingHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler.InnerHandler is not null)
        {
            throw new ArgumentException(
                "The delegating handler must not already have an inner handler.",
                nameof(handler));
        }

        _handlers.Add(handler);
        return this;
    }

    /// <summary>Creates the configured client.</summary>
    /// <returns>
    /// A new client owned by the caller. Dispose it to release its handlers and connection
    /// resources. The client does not own the application factory.
    /// </returns>
    public HttpClient Build()
    {
        var client = _createClient(_options, _handlers);
        if (_user is not null)
        {
            client.AuthenticateAs(_user);
        }

        if (_bearerToken is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        foreach (var header in _headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    /// <summary>
    /// Sends an existing bearer token through the application's configured authentication handler.
    /// This can be used with tokens returned by ASP.NET Core Identity API endpoints.
    /// </summary>
    /// <param name="token">
    /// The non-empty raw bearer token sent without validation. The caller is responsible for using
    /// test credentials and preventing the token from appearing in logs or diagnostics. This value
    /// replaces any configured simulated identity.
    /// </param>
    /// <returns>This builder so additional client behavior can be configured.</returns>
    public TestClientBuilder<TEntryPoint> WithBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _user = null;
        _bearerToken = token;
        return this;
    }

    private TestJwtAuthority GetJwtAuthority() =>
        _authentication.GetJwtAuthority();

    private TestClientBuilder<TEntryPoint> AsJwtCore(
        string? authenticationScheme,
        Action<TestJwtBuilder>? configure) =>
        WithBearerToken(_authentication.GetJwtAuthority(authenticationScheme).CreateToken(configure));

    private TestApiKeyInjectionOptions GetApiKeyOptions() =>
        _authentication.EndToEndApiKey
        ?? throw new InvalidOperationException(
            "End-to-end API-key authentication is not configured. " +
            "Call UseEndToEndApiKey from ConfigureTestAuthentication.");

    private TestClientBuilder<TEntryPoint> WithClientCertificateCore(
        string? authenticationScheme,
        Action<TestClientCertificateBuilder>? configure)
    {
        _ = _authentication.GetClientCertificateRegistration(authenticationScheme);
        var builder = new TestClientCertificateBuilder();
        configure?.Invoke(builder);
        using var certificate = builder.Build();
        return SetClientCertificate(certificate);
    }

    private TestClientBuilder<TEntryPoint> SetClientCertificate(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        _headers[TestAuthenticationDefaults.ClientCertificateHeaderName] =
            Convert.ToBase64String(certificate.Export(
                System.Security.Cryptography.X509Certificates.X509ContentType.Cert));
        return this;
    }
}
