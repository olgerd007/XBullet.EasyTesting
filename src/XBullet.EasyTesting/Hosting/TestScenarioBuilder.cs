using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Fluently arranges state, configures a client, and sends one HTTP request.</summary>
public sealed class TestScenarioBuilder<TEntryPoint>
    where TEntryPoint : class
{
    private readonly TestClientBuilder<TEntryPoint> _client;
    private readonly List<Func<CancellationToken, Task>> _arrangements = [];
    private Func<HttpClient, CancellationToken, Task<HttpResponseMessage>>? _send;
    private bool _executed;

    internal TestScenarioBuilder(AuthenticatedWebApplicationFactory<TEntryPoint> factory)
    {
        _client = factory.Client();
    }

    internal TestScenarioBuilder(TestClientBuilder<TEntryPoint> client)
    {
        _client = client;
    }

    /// <summary>Adds an asynchronous arrangement executed before the client is created.</summary>
    public TestScenarioBuilder<TEntryPoint> Arrange(
        Func<CancellationToken, Task> arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        EnsureNotExecuted();
        _arrangements.Add(arrangement);
        return this;
    }

    /// <summary>Configures the client with a test user.</summary>
    public TestScenarioBuilder<TEntryPoint> AsUser(TestUser user)
    {
        EnsureNotExecuted();
        _client.AsUser(user);
        return this;
    }

    /// <summary>Builds and configures the test user used by the client.</summary>
    public TestScenarioBuilder<TEntryPoint> AsUser(Action<TestUserBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsUser(configure);
        return this;
    }

    /// <summary>Configures the client with an Azure AD-shaped test identity.</summary>
    public TestScenarioBuilder<TEntryPoint> AsAzureAdUser(Action<TestAzureAdUserBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsAzureAdUser(configure);
        return this;
    }

    /// <summary>Configures the client with an API-key-shaped test identity.</summary>
    public TestScenarioBuilder<TEntryPoint> AsApiKey(Action<TestApiKeyBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsApiKey(configure);
        return this;
    }

    /// <summary>Uses a locally signed token with the application's real JWT bearer handler.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwt(Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwt(configure);
        return this;
    }

    /// <summary>Uses a locally signed token from a named JWT authority.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwt(
        string authenticationScheme,
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwt(authenticationScheme, configure);
        return this;
    }

    /// <summary>Uses an expired locally signed token.</summary>
    public TestScenarioBuilder<TEntryPoint> AsExpiredJwt(Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsExpiredJwt(configure);
        return this;
    }

    /// <summary>Uses a deliberately malformed bearer token.</summary>
    public TestScenarioBuilder<TEntryPoint> AsMalformedJwt(string value = "not-a-valid-jwt")
    {
        EnsureNotExecuted();
        _client.AsMalformedJwt(value);
        return this;
    }

    /// <summary>Uses a locally signed token with an invalid audience.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithWrongAudience(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithWrongAudience(configure);
        return this;
    }

    /// <summary>Uses a locally signed token with an invalid issuer.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithWrongIssuer(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithWrongIssuer(configure);
        return this;
    }

    /// <summary>Uses a token with an invalid signature.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithInvalidSignature(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithInvalidSignature(configure);
        return this;
    }

    /// <summary>Uses a token with a key identifier absent from JWKS.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithUnknownKey(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithUnknownKey(configure);
        return this;
    }

    /// <summary>Uses an unsigned token.</summary>
    public TestScenarioBuilder<TEntryPoint> AsUnsignedJwt(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsUnsignedJwt(configure);
        return this;
    }

    /// <summary>Uses a token whose not-before time is in the future.</summary>
    public TestScenarioBuilder<TEntryPoint> AsJwtNotYetValid(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtNotYetValid(configure);
        return this;
    }

    /// <summary>Creates and transports a client certificate to the real certificate handler.</summary>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        Action<TestClientCertificateBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(configure);
        return this;
    }

    /// <summary>Creates a client certificate for a named authentication scheme.</summary>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        Action<TestClientCertificateBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(authenticationScheme, configure);
        return this;
    }

    /// <summary>Transports an existing client certificate to the real certificate handler.</summary>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(certificate);
        return this;
    }

    /// <summary>Transports an existing client certificate for a named authentication scheme.</summary>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(authenticationScheme, certificate);
        return this;
    }

    /// <summary>Injects a real API key into every request header.</summary>
    public TestScenarioBuilder<TEntryPoint> WithApiKeyHeader(string value, string? headerName = null)
    {
        EnsureNotExecuted();
        _client.WithApiKeyHeader(value, headerName);
        return this;
    }

    /// <summary>Injects a real API key into every request query string.</summary>
    public TestScenarioBuilder<TEntryPoint> WithApiKeyQuery(string value, string? parameterName = null)
    {
        EnsureNotExecuted();
        _client.WithApiKeyQuery(value, parameterName);
        return this;
    }

    /// <summary>Configures the scenario client to be anonymous.</summary>
    public TestScenarioBuilder<TEntryPoint> AsAnonymous()
    {
        EnsureNotExecuted();
        _client.AsAnonymous();
        return this;
    }

    /// <summary>Adds or replaces a default request header.</summary>
    public TestScenarioBuilder<TEntryPoint> WithHeader(string name, string value)
    {
        EnsureNotExecuted();
        _client.WithHeader(name, value);
        return this;
    }

    /// <summary>Sets the base address used by relative requests.</summary>
    public TestScenarioBuilder<TEntryPoint> WithBaseAddress(Uri baseAddress)
    {
        EnsureNotExecuted();
        _client.WithBaseAddress(baseAddress);
        return this;
    }

    /// <summary>Prevents automatic HTTP redirect handling.</summary>
    public TestScenarioBuilder<TEntryPoint> WithoutRedirects()
    {
        EnsureNotExecuted();
        _client.WithoutRedirects();
        return this;
    }

    /// <summary>Prevents automatic cookie persistence.</summary>
    public TestScenarioBuilder<TEntryPoint> WithoutCookies()
    {
        EnsureNotExecuted();
        _client.WithoutCookies();
        return this;
    }

    /// <summary>Defines a GET request for this scenario.</summary>
    public TestScenarioBuilder<TEntryPoint> Get(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) => client.GetAsync(requestUri, token));
    }

    /// <summary>Defines a DELETE request for this scenario.</summary>
    public TestScenarioBuilder<TEntryPoint> Delete(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) => client.DeleteAsync(requestUri, token));
    }

    /// <summary>Defines a POST request with a JSON body for this scenario.</summary>
    public TestScenarioBuilder<TEntryPoint> PostJson<T>(string requestUri, T body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) => client.PostAsJsonAsync(requestUri, body, token));
    }

    /// <summary>Defines a PUT request with a JSON body for this scenario.</summary>
    public TestScenarioBuilder<TEntryPoint> PutJson<T>(string requestUri, T body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) => client.PutAsJsonAsync(requestUri, body, token));
    }

    /// <summary>Defines a custom HTTP request operation for this scenario.</summary>
    public TestScenarioBuilder<TEntryPoint> Send(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        EnsureNotExecuted();
        if (_send is not null)
        {
            throw new InvalidOperationException("The test scenario already has an HTTP request.");
        }

        _send = send;
        return this;
    }

    /// <summary>Runs the arrangements and sends the configured request once.</summary>
    public async Task<TestScenarioResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureNotExecuted();
        var send = _send ?? throw new InvalidOperationException(
            "Configure an HTTP request before executing the test scenario.");
        _executed = true;

        foreach (var arrangement in _arrangements)
        {
            await arrangement(cancellationToken);
        }

        var client = _client.Build();
        try
        {
            var response = await send(client, cancellationToken)
                ?? throw new InvalidOperationException("The test scenario request returned null.");
            return new TestScenarioResult(client, response);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private void EnsureNotExecuted()
    {
        if (_executed)
        {
            throw new InvalidOperationException("The test scenario has already been executed.");
        }
    }
}
