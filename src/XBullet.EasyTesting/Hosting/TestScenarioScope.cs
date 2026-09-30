using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Authentication;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// Owns the isolated host and mutable test state used by one integration-test scenario.
/// </summary>
/// <typeparam name="TEntryPoint">
/// The application entry-point type used by <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </typeparam>
public sealed class TestScenarioScope<TEntryPoint> : IAsyncDisposable
    where TEntryPoint : class
{
    private readonly AuthenticatedWebApplicationFactory<TEntryPoint> _owner;
    private readonly WebApplicationFactory<TEntryPoint> _host;
    private readonly TestScenarioContext _context;
    private bool _disposed;
    private Exception? _testFailure;

    internal TestScenarioScope(
        AuthenticatedWebApplicationFactory<TEntryPoint> owner,
        WebApplicationFactory<TEntryPoint> host,
        TestScenarioContext context)
    {
        _owner = owner;
        _host = host;
        _context = context;
    }

    /// <summary>Gets the unique identifier for this scenario.</summary>
    /// <value>A non-empty identifier that remains stable for the lifetime of this scope.</value>
    public string ScenarioId => _context.ScenarioId;

    /// <summary>Gets services from the scenario-specific host.</summary>
    /// <value>
    /// The root service provider owned by this scope's host. The caller must not dispose it.
    /// </value>
    public IServiceProvider Services => _host.Services;

    /// <summary>Gets diagnostics captured for a failed scenario, when available.</summary>
    /// <value>
    /// The captured diagnostics after a scenario failure, or <see langword="null"/> before a
    /// failure has been captured. The scope owns the diagnostic object.
    /// </value>
    public TestScenarioDiagnostics? Diagnostics { get; private set; }

    /// <summary>Creates an anonymous client from the isolated scenario host.</summary>
    /// <param name="options">
    /// Client options to apply. When <see langword="null"/>, the host's default client options are
    /// used. The options object is read but not owned or mutated by this method.
    /// </param>
    /// <returns>
    /// A new anonymous client connected to the scenario host. The caller owns and must dispose the
    /// client.
    /// </returns>
    public HttpClient CreateAnonymousClient(WebApplicationFactoryClientOptions? options = null) =>
        options is null ? _host.CreateClient() : _host.CreateClient(options);

    /// <summary>Creates an authenticated client from the isolated scenario host.</summary>
    /// <param name="user">
    /// The simulated identity sent by the client. When <see langword="null"/>, a default
    /// authenticated test user is created. The supplied user is read but not owned or mutated.
    /// </param>
    /// <param name="options">
    /// Client options to apply. When <see langword="null"/>, the host's default client options are
    /// used. The options object is read but not owned or mutated by this method.
    /// </param>
    /// <returns>
    /// A new authenticated client connected to the scenario host. The caller owns and must dispose
    /// the client.
    /// </returns>
    public HttpClient CreateAuthenticatedClient(
        TestUser? user = null,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = CreateAnonymousClient(options);
        return client.AuthenticateAs(user ?? new TestUser());
    }

    /// <summary>Starts a fluent client definition for the isolated scenario host.</summary>
    /// <returns>
    /// A new mutable client builder. Clients produced by the builder are owned by the caller.
    /// </returns>
    public TestClientBuilder<TEntryPoint> Client() =>
        new(
            (options, handlers) => TestHttpClientFactory.Create(_host, options, handlers),
            _owner.GetAuthenticationConfigurationForClient());

    /// <summary>Starts a fluent arrange, client, and HTTP request definition in this scope.</summary>
    /// <returns>A new mutable scenario builder associated with this scope's isolated host.</returns>
    public TestScenarioBuilder<TEntryPoint> Scenario() => new(Client(), () => Services);

    /// <summary>Gets a configured local JWT authority, optionally by authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The exact registered authentication scheme to select. When <see langword="null"/>, the
    /// default local JWT authority is returned.
    /// </param>
    /// <returns>
    /// The factory-owned authority for the selected scheme. The caller must not dispose it.
    /// </returns>
    public TestJwtAuthority JwtAuthority(string? authenticationScheme = null) =>
        _owner.JwtAuthority(authenticationScheme);

    /// <summary>Gets authentication events captured from real handlers in this scenario.</summary>
    /// <value>
    /// The factory-owned event recorder shared with the configured authentication handlers. The
    /// caller must not dispose it.
    /// </value>
    public TestAuthenticationEventRecorder AuthenticationEvents => _owner.AuthenticationEvents;

    /// <summary>Gets a named external dependency created for this scenario.</summary>
    /// <typeparam name="TResource">The expected concrete environment-resource type.</typeparam>
    /// <param name="name">
    /// The non-empty registered resource name. Matching is case-insensitive.
    /// </param>
    /// <returns>
    /// The scenario-owned resource registered under <paramref name="name"/>. The caller must not
    /// dispose it.
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// No environment resource is registered with <paramref name="name"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The named resource is not assignable to <typeparamref name="TResource"/>.
    /// </exception>
    public TResource GetEnvironmentResource<TResource>(string name)
        where TResource : class, ITestScenarioEnvironmentResource =>
        _context.GetEnvironmentResource<TResource>(name);

    internal IReadOnlyList<TestScenarioEnvironmentResourceRegistration> EnvironmentResources =>
        _context.EnvironmentResources;

    internal async Task CaptureFailureAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _testFailure = exception;
        Diagnostics = await _owner.CaptureScenarioDiagnosticsInternalAsync(
            this,
            cancellationToken);
        exception.Data[TestScenarioDiagnostics.ExceptionDataKey] = Diagnostics;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        try
        {
            await _owner.CleanupScenarioInternalAsync(this, CancellationToken.None);
        }
        catch (Exception exception)
        {
            failures = [exception];
        }

        try
        {
            await _host.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }

        try
        {
            await _context.CleanupAsync();
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }

        try
        {
            await _owner.ResetScenarioResourcesInternalAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        finally
        {
            _owner.ReleaseScenarioScope();
        }

        if (failures is null)
        {
            return;
        }

        var cleanupFailure = new AggregateException(
            "One or more operations failed while cleaning up the test scenario.",
            failures);
        if (_testFailure is not null)
        {
            _testFailure.Data["XBullet.EasyTesting.TestScenarioCleanupException"] = cleanupFailure;
            return;
        }

        throw cleanupFailure;
    }
}
