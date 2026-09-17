using XBullet.EasyTesting.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Fluently configures a client created by an authenticated application factory.</summary>
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
    public TestClientBuilder<TEntryPoint> AsUser(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _user = user;
        _bearerToken = null;
        return this;
    }

    /// <summary>Builds and configures the test identity used by this client.</summary>
    public TestClientBuilder<TEntryPoint> AsUser(Action<TestUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = TestUser.CreateBuilder();
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds an Azure AD-shaped identity for this client.</summary>
    public TestClientBuilder<TEntryPoint> AsAzureAdUser(Action<TestAzureAdUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestAzureAdUserBuilder(_azureAdAuthenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds an API-key-shaped identity for this client.</summary>
    public TestClientBuilder<TEntryPoint> AsApiKey(Action<TestApiKeyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestApiKeyBuilder(_apiKeyAuthenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds a Federation identity with an additional API-user identity.</summary>
    public TestClientBuilder<TEntryPoint> AsFederatedUser(
        Action<TestFederatedUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestFederatedUserBuilder(_federationAuthenticationScheme);
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Creates and sends a locally signed bearer token through the application's real JWT handler.</summary>
    public TestClientBuilder<TEntryPoint> AsJwt(Action<TestJwtBuilder>? configure = null) =>
        AsJwtCore(authenticationScheme: null, configure);

    /// <summary>Creates a token for a named local authority and sends it through the real JWT handler.</summary>
    public TestClientBuilder<TEntryPoint> AsJwt(
        string authenticationScheme,
        Action<TestJwtBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        return AsJwtCore(authenticationScheme, configure);
    }

    /// <summary>Sends an expired locally signed bearer token.</summary>
    public TestClientBuilder<TEntryPoint> AsExpiredJwt(Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateExpiredToken(configure));

    /// <summary>Sends a locally signed bearer token with an invalid audience.</summary>
    public TestClientBuilder<TEntryPoint> AsJwtWithWrongAudience(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateWrongAudienceToken(configure));

    /// <summary>Sends a locally signed bearer token with an invalid issuer.</summary>
    public TestClientBuilder<TEntryPoint> AsJwtWithWrongIssuer(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateWrongIssuerToken(configure));

    /// <summary>Sends a deliberately malformed bearer token.</summary>
    public TestClientBuilder<TEntryPoint> AsMalformedJwt(string value = "not-a-valid-jwt") =>
        WithBearerToken(value);

    /// <summary>Sends a token whose signature is invalid for a known key identifier.</summary>
    public TestClientBuilder<TEntryPoint> AsJwtWithInvalidSignature(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateInvalidSignatureToken(configure));

    /// <summary>Sends a token whose key identifier is absent from JWKS.</summary>
    public TestClientBuilder<TEntryPoint> AsJwtWithUnknownKey(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateUnknownKeyToken(configure));

    /// <summary>Sends an unsigned JWT.</summary>
    public TestClientBuilder<TEntryPoint> AsUnsignedJwt(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateUnsignedToken(configure));

    /// <summary>Sends a token whose not-before time is in the future.</summary>
    public TestClientBuilder<TEntryPoint> AsJwtNotYetValid(
        Action<TestJwtBuilder>? configure = null) =>
        WithBearerToken(GetJwtAuthority().CreateFutureNotBeforeToken(configure));

    /// <summary>Injects a real API key into every request header.</summary>
    public TestClientBuilder<TEntryPoint> WithApiKeyHeader(string value, string? headerName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var options = GetApiKeyOptions();
        return WithHeader(headerName ?? options.HeaderName, value);
    }

    /// <summary>Injects a real API key into every request query string.</summary>
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
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        Action<TestClientCertificateBuilder>? configure = null) =>
        WithClientCertificateCore(authenticationScheme: null, configure);

    /// <summary>Creates a client certificate for a named certificate authentication scheme.</summary>
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        Action<TestClientCertificateBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        return WithClientCertificateCore(authenticationScheme, configure);
    }

    /// <summary>Transports an existing client certificate to the real certificate handler.</summary>
    public TestClientBuilder<TEntryPoint> WithClientCertificate(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _ = _authentication.GetClientCertificateRegistration();
        return SetClientCertificate(certificate);
    }

    /// <summary>Transports an existing client certificate for a named authentication scheme.</summary>
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
    public TestClientBuilder<TEntryPoint> AsAnonymous()
    {
        _user = null;
        _bearerToken = null;
        return this;
    }

    /// <summary>Sets the base address used by relative requests.</summary>
    public TestClientBuilder<TEntryPoint> WithBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _options.BaseAddress = baseAddress;
        return this;
    }

    /// <summary>Prevents automatic HTTP redirect handling.</summary>
    public TestClientBuilder<TEntryPoint> WithoutRedirects()
    {
        _options.AllowAutoRedirect = false;
        return this;
    }

    /// <summary>Prevents automatic cookie persistence.</summary>
    public TestClientBuilder<TEntryPoint> WithoutCookies()
    {
        _options.HandleCookies = false;
        return this;
    }

    /// <summary>Adds or replaces a default request header.</summary>
    public TestClientBuilder<TEntryPoint> WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = value;
        return this;
    }

    /// <summary>Creates the configured client.</summary>
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

    private TestClientBuilder<TEntryPoint> WithBearerToken(string token)
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
