using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Owns one serialized Functions test scenario, resource resets, and registered cleanup.</summary>
/// <remarks>The host is borrowed; invocations keep their own DI scopes. Dispose the scenario before its host.</remarks>
public sealed class AzureFunctionTestScenarioScope : IAsyncDisposable
{
    private readonly AzureFunctionTestHost _host;
    private Exception? _failure;
    private bool _disposed;

    internal AzureFunctionTestScenarioScope(AzureFunctionTestHost host)
    {
        _host = host;
        Context = new TestScenarioContext(Guid.NewGuid().ToString("N"));
    }

    /// <summary>Gets the borrowed function host while this scenario is active.</summary>
    /// <value>The caller-owned host used for contexts, trigger builders, and invocations.</value>
    public AzureFunctionTestHost Host
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _host;
        }
    }

    /// <summary>Gets this scenario's stable identifier.</summary>
    /// <value>The unique identifier generated at scope creation.</value>
    public string ScenarioId => Context.ScenarioId;

    /// <summary>Gets cleanup registration for scenario-owned objects and actions.</summary>
    /// <value>The context owned by this scope; use it only while the scenario is active.</value>
    public TestScenarioContext Context { get; }

    /// <summary>Gets failure diagnostics captured by the scenario runner.</summary>
    /// <value>The captured diagnostics, or null when the runner has not observed a failure.</value>
    public TestScenarioDiagnostics? Diagnostics { get; private set; }

    /// <summary>Gets a borrowed, named resource registered on the function host.</summary>
    /// <typeparam name="TResource">The expected resource type.</typeparam>
    /// <param name="name">The non-empty, case-insensitively matched resource name.</param>
    /// <returns>The caller-owned resource; this scenario resets but never disposes it.</returns>
    public TResource GetResource<TResource>(string name) where TResource : class, ITestScenarioResource =>
        Host.GetResource<TResource>(name);

    internal async Task CaptureFailureAsync(Exception exception)
    {
        _failure = exception;
        Diagnostics = exception.Data[TestScenarioDiagnostics.ExceptionDataKey] is TestScenarioDiagnostics existing &&
            existing.ScenarioId == ScenarioId
            ? existing
            : await _host.CaptureDiagnosticsAsync(ScenarioId).ConfigureAwait(false);
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
            await Context.CleanupAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures = [exception];
        }

        try
        {
            await _host.ResetResourcesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }
        finally
        {
            _host.ReleaseScenario();
        }

        if (failures is not null)
        {
            var cleanup = new AggregateException("One or more Functions scenario cleanup operations failed.", failures);
            if (_failure is null)
            {
                throw cleanup;
            }

            AzureFunctionTestHost.AddCleanupFailure(_failure, cleanup);
        }
    }
}
