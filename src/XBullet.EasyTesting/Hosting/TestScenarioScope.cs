using XBullet.EasyTesting.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// Owns the isolated host and mutable test state used by one integration-test scenario.
/// </summary>
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
    public string ScenarioId => _context.ScenarioId;

    /// <summary>Gets services from the scenario-specific host.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>Gets diagnostics captured for a failed scenario, when available.</summary>
    public TestScenarioDiagnostics? Diagnostics { get; private set; }

    /// <summary>Creates an anonymous client from the isolated scenario host.</summary>
    public HttpClient CreateAnonymousClient(WebApplicationFactoryClientOptions? options = null) =>
        options is null ? _host.CreateClient() : _host.CreateClient(options);

    /// <summary>Creates an authenticated client from the isolated scenario host.</summary>
    public HttpClient CreateAuthenticatedClient(
        TestUser? user = null,
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = CreateAnonymousClient(options);
        return client.AuthenticateAs(user ?? new TestUser());
    }

    /// <summary>Starts a fluent client definition for the isolated scenario host.</summary>
    public TestClientBuilder<TEntryPoint> Client() =>
        new(
            (options, handlers) => TestHttpClientFactory.Create(_host, options, handlers),
            _owner.GetAuthenticationConfigurationForClient());

    /// <summary>Starts a fluent arrange, client, and HTTP request definition in this scope.</summary>
    public TestScenarioBuilder<TEntryPoint> Scenario() => new(Client());

    /// <summary>Gets a configured local JWT authority, optionally by authentication scheme.</summary>
    public TestJwtAuthority JwtAuthority(string? authenticationScheme = null) =>
        _owner.JwtAuthority(authenticationScheme);

    /// <summary>Gets authentication events captured from real handlers in this scenario.</summary>
    public TestAuthenticationEventRecorder AuthenticationEvents => _owner.AuthenticationEvents;

    /// <summary>Gets a named external dependency created for this scenario.</summary>
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
