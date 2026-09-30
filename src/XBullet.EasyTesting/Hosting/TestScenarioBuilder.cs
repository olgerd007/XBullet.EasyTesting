using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XBullet.EasyTesting.Authentication;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Fluently arranges state, configures a client, and sends one HTTP request.</summary>
/// <remarks>
/// A builder is single-use. After <see cref="ExecuteAsync"/> begins, configuration methods and
/// subsequent execution attempts throw <see cref="InvalidOperationException"/>. The mutable builder
/// is not thread-safe and must be configured and executed from one execution flow.
/// </remarks>
/// <typeparam name="TEntryPoint">
/// The application entry-point type used by the factory that creates the scenario client.
/// </typeparam>
public sealed class TestScenarioBuilder<TEntryPoint>
    where TEntryPoint : class
{
    private readonly TestClientBuilder<TEntryPoint> _client;
    private readonly List<Func<CancellationToken, Task>> _arrangements = [];
    private readonly Func<JsonSerializerOptions?>? _resolveApplicationJsonOptions;
    private Func<HttpClient, CancellationToken, Task<HttpResponseMessage>>? _send;
    private JsonSerializerOptions? _jsonOptions;
    private bool _executed;

    internal TestScenarioBuilder(AuthenticatedWebApplicationFactory<TEntryPoint> factory)
        : this(factory.Client(), () => factory.Services)
    {
    }

    internal TestScenarioBuilder(
        TestClientBuilder<TEntryPoint> client,
        Func<IServiceProvider>? resolveServices)
    {
        _client = client;
        _resolveApplicationJsonOptions = resolveServices is null
            ? null
            : () => ResolveApplicationJsonOptions(resolveServices());
    }

    /// <summary>Adds an asynchronous arrangement executed before the client is created.</summary>
    /// <param name="arrangement">
    /// The callback invoked once during execution with the execution cancellation token.
    /// Arrangements run sequentially in registration order and are not invoked concurrently by this
    /// builder.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> Arrange(
        Func<CancellationToken, Task> arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        EnsureNotExecuted();
        _arrangements.Add(arrangement);
        return this;
    }

    /// <summary>Configures the client with a test user.</summary>
    /// <param name="user">
    /// The non-null simulated identity sent by the scenario client. The immutable value is retained
    /// but not owned, and it replaces any configured bearer token.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsUser(TestUser user)
    {
        EnsureNotExecuted();
        _client.AsUser(user);
        return this;
    }

    /// <summary>Builds and configures the test user used by the client.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new test-user builder. It is not retained or
    /// invoked concurrently.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsUser(Action<TestUserBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsUser(configure);
        return this;
    }

    /// <summary>Builds a test identity that targets a named simulated scheme.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new test-user builder initialized for
    /// <paramref name="authenticationScheme"/>.
    /// </param>
    /// <param name="authenticationScheme">
    /// The non-empty application authentication-scheme name written to the transported identity.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsUser(
        Action<TestUserBuilder> configure,
        string authenticationScheme)
    {
        EnsureNotExecuted();
        _client.AsUser(configure, authenticationScheme);
        return this;
    }

    /// <summary>Configures the client with an Azure AD-shaped test identity.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new Azure AD user builder targeting the
    /// configured simulated scheme.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsAzureAdUser(Action<TestAzureAdUserBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsAzureAdUser(configure);
        return this;
    }

    /// <summary>Configures the client with an API-key-shaped test identity.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new API-key identity builder. It configures
    /// simulated claims, not a real API-key secret.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsApiKey(Action<TestApiKeyBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsApiKey(configure);
        return this;
    }

    /// <summary>Configures the client with Federation and API-user test identities.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new Federation user builder targeting the
    /// configured simulated scheme.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsFederatedUser(
        Action<TestFederatedUserBuilder> configure)
    {
        EnsureNotExecuted();
        _client.AsFederatedUser(configure);
        return this;
    }

    /// <summary>Uses a locally signed token with the application's real JWT bearer handler.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously before signing. When <see langword="null"/>, the local
    /// authority's default valid token is used. The callback runs once and is not retained.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwt(Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwt(configure);
        return this;
    }

    /// <summary>Sends an existing bearer token through the application's configured handler.</summary>
    /// <param name="token">
    /// The non-empty raw bearer token sent without validation. Use test credentials and prevent the
    /// token from appearing in logs or diagnostics. It replaces any simulated identity.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithBearerToken(string token)
    {
        EnsureNotExecuted();
        _client.WithBearerToken(token);
        return this;
    }

    /// <summary>Uses a locally signed token from a named JWT authority.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty registered JWT authentication scheme whose local authority signs the token.
    /// </param>
    /// <param name="configure">
    /// Configures token claims synchronously before signing. When <see langword="null"/>, the
    /// selected authority's default valid token is used.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwt(
        string authenticationScheme,
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwt(authenticationScheme, configure);
        return this;
    }

    /// <summary>Uses an expired locally signed token.</summary>
    /// <param name="configure">
    /// Configures claims after the expired lifetime is applied and before signing. When
    /// <see langword="null"/>, the expired defaults remain unchanged; the callback can replace the
    /// configured lifetime.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsExpiredJwt(Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsExpiredJwt(configure);
        return this;
    }

    /// <summary>Uses a deliberately malformed bearer token.</summary>
    /// <param name="value">
    /// The non-empty raw bearer-token value. When omitted, <c>not-a-valid-jwt</c> is used.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsMalformedJwt(string value = "not-a-valid-jwt")
    {
        EnsureNotExecuted();
        _client.AsMalformedJwt(value);
        return this;
    }

    /// <summary>Uses a locally signed token with an invalid audience.</summary>
    /// <param name="configure">
    /// Configures claims after the invalid audience is applied and before signing. When
    /// <see langword="null"/>, the invalid audience remains unchanged; the callback can replace it.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithWrongAudience(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithWrongAudience(configure);
        return this;
    }

    /// <summary>Uses a locally signed token with an invalid issuer.</summary>
    /// <param name="configure">
    /// Configures claims after the invalid issuer is applied and before signing. When
    /// <see langword="null"/>, the invalid issuer remains unchanged; the callback can replace it.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithWrongIssuer(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithWrongIssuer(configure);
        return this;
    }

    /// <summary>Uses a token with an invalid signature.</summary>
    /// <param name="configure">
    /// Configures claims before signing with an untrusted key whose identifier matches the current
    /// trusted key. When <see langword="null"/>, default claims are used. The callback cannot replace
    /// the untrusted signing key.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithInvalidSignature(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithInvalidSignature(configure);
        return this;
    }

    /// <summary>Uses a token with a key identifier absent from JWKS.</summary>
    /// <param name="configure">
    /// Configures claims before signing with an untrusted key whose identifier is absent from JWKS.
    /// When <see langword="null"/>, default claims are used. The callback cannot replace the
    /// untrusted signing key.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwtWithUnknownKey(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtWithUnknownKey(configure);
        return this;
    }

    /// <summary>Uses an unsigned token.</summary>
    /// <param name="configure">
    /// Configures token claims synchronously before serialization. When <see langword="null"/>, the
    /// local authority's default claims are used. No signing key is applied.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsUnsignedJwt(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsUnsignedJwt(configure);
        return this;
    }

    /// <summary>Uses a token whose not-before time is in the future.</summary>
    /// <param name="configure">
    /// Configures claims after the future lifetime is applied and before signing. When
    /// <see langword="null"/>, the future not-before time remains unchanged; the callback can
    /// replace the configured lifetime.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> AsJwtNotYetValid(
        Action<TestJwtBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.AsJwtNotYetValid(configure);
        return this;
    }

    /// <summary>Creates and transports a client certificate to the real certificate handler.</summary>
    /// <param name="configure">
    /// Configures a new self-signed certificate synchronously before export. When
    /// <see langword="null"/>, certificate-builder defaults are used. The callback runs once and is
    /// not retained.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        Action<TestClientCertificateBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(configure);
        return this;
    }

    /// <summary>Creates a client certificate for a named authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty registered certificate authentication scheme that accepts the transported
    /// certificate.
    /// </param>
    /// <param name="configure">
    /// Configures a new self-signed certificate synchronously before export. When
    /// <see langword="null"/>, certificate-builder defaults are used.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        Action<TestClientCertificateBuilder>? configure = null)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(authenticationScheme, configure);
        return this;
    }

    /// <summary>Transports an existing client certificate to the real certificate handler.</summary>
    /// <param name="certificate">
    /// The certificate exported immediately into the internal transport header. The scenario does
    /// not retain, own, or dispose it.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(certificate);
        return this;
    }

    /// <summary>Transports an existing client certificate for a named authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty registered certificate authentication scheme that accepts the transported
    /// certificate.
    /// </param>
    /// <param name="certificate">
    /// The certificate exported immediately into the internal transport header. The scenario does
    /// not retain, own, or dispose it.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithClientCertificate(
        string authenticationScheme,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        EnsureNotExecuted();
        _client.WithClientCertificate(authenticationScheme, certificate);
        return this;
    }

    /// <summary>Injects a real API key into every request header.</summary>
    /// <param name="value">
    /// The non-empty API-key secret sent without validation. Use test-only credentials and prevent
    /// the value from appearing in logs or diagnostics.
    /// </param>
    /// <param name="headerName">
    /// The header name to use. When <see langword="null"/>, the name configured for end-to-end
    /// API-key authentication is used.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithApiKeyHeader(string value, string? headerName = null)
    {
        EnsureNotExecuted();
        _client.WithApiKeyHeader(value, headerName);
        return this;
    }

    /// <summary>Injects a real API key into every request query string.</summary>
    /// <param name="value">
    /// The non-empty API-key secret appended without validation. Query strings can be logged by
    /// applications and infrastructure, so use test-only credentials.
    /// </param>
    /// <param name="parameterName">
    /// The query-parameter name to use. When <see langword="null"/>, the configured end-to-end
    /// API-key parameter name is used.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithApiKeyQuery(string value, string? parameterName = null)
    {
        EnsureNotExecuted();
        _client.WithApiKeyQuery(value, parameterName);
        return this;
    }

    /// <summary>Configures the scenario client to be anonymous.</summary>
    /// <returns>
    /// This builder. Any selected simulated identity or bearer token is removed; other configured
    /// credentials and headers remain unchanged.
    /// </returns>
    public TestScenarioBuilder<TEntryPoint> AsAnonymous()
    {
        EnsureNotExecuted();
        _client.AsAnonymous();
        return this;
    }

    /// <summary>Adds or replaces a default request header.</summary>
    /// <param name="name">
    /// The non-empty header name. Matching existing configured names is case-insensitive.
    /// </param>
    /// <param name="value">
    /// The non-null header value. HTTP header validation occurs when the client is created.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithHeader(string name, string value)
    {
        EnsureNotExecuted();
        _client.WithHeader(name, value);
        return this;
    }

    /// <summary>Configures JSON serialization for the scenario's request helpers.</summary>
    /// <param name="options">
    /// The non-null options retained and used by <see cref="PostJson{T}(string, T)"/> and
    /// <see cref="PutJson{T}(string, T)"/> when the scenario executes. This replaces any previously
    /// resolved application or configured scenario options. Per-request overloads that accept
    /// options take precedence.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public TestScenarioBuilder<TEntryPoint> WithJsonOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureNotExecuted();
        _jsonOptions = options;
        return this;
    }

    /// <summary>Adds a delegating handler to the scenario client's HTTP pipeline.</summary>
    /// <param name="handler">
    /// The handler added after built-in redirect and cookie handlers, in registration order. Its
    /// <see cref="DelegatingHandler.InnerHandler"/> must be <see langword="null"/>. Ownership
    /// transfers to the client created during <see cref="ExecuteAsync(CancellationToken)"/>, which
    /// disposes it with the pipeline.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithHandler(DelegatingHandler handler)
    {
        EnsureNotExecuted();
        _client.WithHandler(handler);
        return this;
    }

    /// <summary>Sets the base address used by relative requests.</summary>
    /// <param name="baseAddress">
    /// The non-null base URI passed unchanged to the client factory. The scenario retains the
    /// immutable URI but does not own it.
    /// </param>
    /// <returns>This builder so additional scenario steps can be configured.</returns>
    public TestScenarioBuilder<TEntryPoint> WithBaseAddress(Uri baseAddress)
    {
        EnsureNotExecuted();
        _client.WithBaseAddress(baseAddress);
        return this;
    }

    /// <summary>Prevents automatic HTTP redirect handling.</summary>
    /// <returns>This builder with automatic redirects disabled for the scenario client.</returns>
    public TestScenarioBuilder<TEntryPoint> WithoutRedirects()
    {
        EnsureNotExecuted();
        _client.WithoutRedirects();
        return this;
    }

    /// <summary>Prevents automatic cookie persistence.</summary>
    /// <returns>This builder with automatic cookie handling disabled for the scenario client.</returns>
    public TestScenarioBuilder<TEntryPoint> WithoutCookies()
    {
        EnsureNotExecuted();
        _client.WithoutCookies();
        return this;
    }

    /// <summary>Defines a GET request for this scenario.</summary>
    /// <param name="requestUri">
    /// The non-empty relative or absolute request URI passed to <see cref="HttpClient.GetAsync(string, CancellationToken)"/>.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
    public TestScenarioBuilder<TEntryPoint> Get(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) => client.GetAsync(requestUri, token));
    }

    /// <summary>Defines a DELETE request for this scenario.</summary>
    /// <param name="requestUri">
    /// The non-empty relative or absolute request URI passed to <see cref="HttpClient.DeleteAsync(string, CancellationToken)"/>.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
    public TestScenarioBuilder<TEntryPoint> Delete(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) => client.DeleteAsync(requestUri, token));
    }

    /// <summary>Defines a POST request with a JSON body for this scenario.</summary>
    /// <typeparam name="T">The type serialized as the JSON request body.</typeparam>
    /// <param name="requestUri">The non-empty relative or absolute request URI.</param>
    /// <param name="body">
    /// The value serialized with per-scenario application JSON options or options configured by
    /// <see cref="WithJsonOptions"/>. When neither is available, the web defaults used by
    /// <see cref="HttpClientJsonExtensions.PostAsJsonAsync{TValue}(HttpClient, string, TValue, CancellationToken)"/>
    /// apply.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
    public TestScenarioBuilder<TEntryPoint> PostJson<T>(string requestUri, T body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) =>
        {
            var options = _jsonOptions ?? _resolveApplicationJsonOptions?.Invoke();
            return options is null
                ? client.PostAsJsonAsync(requestUri, body, token)
                : client.PostAsJsonAsync(requestUri, body, options, token);
        });
    }

    /// <summary>Defines a POST request with a JSON body and explicit serialization options.</summary>
    /// <typeparam name="T">The type serialized as the JSON request body.</typeparam>
    /// <param name="requestUri">The non-empty relative or absolute request URI.</param>
    /// <param name="body">The value serialized as the JSON request body.</param>
    /// <param name="options">
    /// The non-null JSON options used when the scenario executes. Use the same converters as the
    /// application when request and response payloads must use the same enum representation.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
    public TestScenarioBuilder<TEntryPoint> PostJson<T>(
        string requestUri,
        T body,
        JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(options);
        return Send((client, token) => client.PostAsJsonAsync(requestUri, body, options, token));
    }

    /// <summary>Defines a PUT request with a JSON body for this scenario.</summary>
    /// <typeparam name="T">The type serialized as the JSON request body.</typeparam>
    /// <param name="requestUri">The non-empty relative or absolute request URI.</param>
    /// <param name="body">
    /// The value serialized with per-scenario application JSON options or options configured by
    /// <see cref="WithJsonOptions"/>. When neither is available, the web defaults used by
    /// <see cref="HttpClientJsonExtensions.PutAsJsonAsync{TValue}(HttpClient, string, TValue, CancellationToken)"/>
    /// apply.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
    public TestScenarioBuilder<TEntryPoint> PutJson<T>(string requestUri, T body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        return Send((client, token) =>
        {
            var options = _jsonOptions ?? _resolveApplicationJsonOptions?.Invoke();
            return options is null
                ? client.PutAsJsonAsync(requestUri, body, token)
                : client.PutAsJsonAsync(requestUri, body, options, token);
        });
    }

    /// <summary>Defines a PUT request with a JSON body and explicit serialization options.</summary>
    /// <typeparam name="T">The type serialized as the JSON request body.</typeparam>
    /// <param name="requestUri">The non-empty relative or absolute request URI.</param>
    /// <param name="body">The value serialized as the JSON request body.</param>
    /// <param name="options">
    /// The non-null JSON options used when the scenario executes. Use the same converters as the
    /// application when request and response payloads must use the same enum representation.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
    public TestScenarioBuilder<TEntryPoint> PutJson<T>(
        string requestUri,
        T body,
        JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(options);
        return Send((client, token) => client.PutAsJsonAsync(requestUri, body, options, token));
    }

    /// <summary>Defines a custom HTTP request operation for this scenario.</summary>
    /// <param name="send">
    /// The callback invoked once after all arrangements complete and the scenario client is
    /// created. It receives the result-owned client and execution cancellation token, must return a
    /// non-null response, and is not invoked concurrently by this builder. On failure, the client is
    /// disposed before the exception is propagated.
    /// </param>
    /// <returns>This builder with its single HTTP request configured.</returns>
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
    /// <param name="cancellationToken">
    /// Cancels the registered arrangements and request callback. On cancellation or any other
    /// failure, the created client is disposed. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result owns the created client and non-null response. Dispose the result to
    /// dispose both. The builder cannot be executed or modified again, including after failure.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No request is configured, the request callback returns <see langword="null"/>, or this
    /// builder has already begun execution.
    /// </exception>
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

    private static JsonSerializerOptions ResolveApplicationJsonOptions(IServiceProvider services)
    {
        if (services.GetService<IActionDescriptorCollectionProvider>() is not null)
        {
            return services.GetRequiredService<IOptions<MvcJsonOptions>>()
                .Value.JsonSerializerOptions;
        }

        return services.GetRequiredService<IOptions<HttpJsonOptions>>()
            .Value.SerializerOptions;
    }
}
