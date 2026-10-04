using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Provides function instances, invocation scopes, and scenario resource lifecycles.</summary>
public sealed partial class AzureFunctionTestHost
{
    internal const string CleanupExceptionDataKey = "XBullet.EasyTesting.TestScenarioCleanupException";
    private readonly SemaphoreSlim _scenarioGate = new(1, 1);
    private readonly IReadOnlyDictionary<string, ITestScenarioResource> _resources;
    private AzureFunctionTestScenarioScope? _activeScenario;
    private bool _disposed;

    /// <summary>Starts an isolated resource lifecycle while retaining per-invocation DI scopes.</summary>
    /// <param name="cancellationToken">Cancels gate acquisition and initial resource resets.</param>
    /// <returns>A caller-owned scenario scope that must be asynchronously disposed.</returns>
    /// <remarks>Scenarios on one host are serialized. Nested scenarios on the same host are unsupported.</remarks>
    public async Task<AzureFunctionTestScenarioScope> CreateTestScenarioScopeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _scenarioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_disposed)
        {
            _scenarioGate.Release();
            throw new ObjectDisposedException(nameof(AzureFunctionTestHost));
        }

        var scope = new AzureFunctionTestScenarioScope(this);
        _activeScenario = scope;
        try
        {
            await ResetResourcesAsync(cancellationToken).ConfigureAwait(false);
            return scope;
        }
        catch (Exception exception)
        {
            await scope.CaptureFailureAsync(exception).ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Runs a test in a fresh scenario with automatic diagnostics and cleanup.</summary>
    /// <param name="test">The non-null callback borrowing the scope.</param>
    /// <returns>A task completing after test execution and cleanup.</returns>
    public Task RunInTestScenarioScopeAsync(Func<AzureFunctionTestScenarioScope, CancellationToken, Task> test) =>
        RunInTestScenarioScopeAsync(test, CancellationToken.None);

    /// <summary>Runs a test in a fresh scenario with automatic diagnostics and cleanup.</summary>
    /// <param name="test">The non-null callback borrowing the scope and token.</param>
    /// <param name="cancellationToken">Cancels scope initialization and is passed to the callback.</param>
    /// <returns>A task completing after test execution and cleanup.</returns>
    /// <remarks>Failure diagnostics and cleanup use non-cancelable tokens and preserve the original test exception.</remarks>
    public async Task RunInTestScenarioScopeAsync(
        Func<AzureFunctionTestScenarioScope, CancellationToken, Task> test,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);
        await using var scope = await CreateTestScenarioScopeAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await test(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await scope.CaptureFailureAsync(exception).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Runs a test function and returns its detached result after cleanup.</summary>
    /// <typeparam name="TResult">The detached result type.</typeparam>
    /// <param name="test">The non-null callback borrowing the scope.</param>
    /// <returns>The callback's value after scenario cleanup.</returns>
    public Task<TResult> RunInTestScenarioScopeAsync<TResult>(
        Func<AzureFunctionTestScenarioScope, CancellationToken, Task<TResult>> test) =>
        RunInTestScenarioScopeAsync(test, CancellationToken.None);

    /// <summary>Runs a test function and returns its detached result after cleanup.</summary>
    /// <typeparam name="TResult">The detached result type; do not return scenario-owned objects.</typeparam>
    /// <param name="test">The non-null callback borrowing the scope and token.</param>
    /// <param name="cancellationToken">Cancels scope initialization and is passed to the callback.</param>
    /// <returns>The callback's value after scenario cleanup.</returns>
    /// <remarks>Failure diagnostics and cleanup use non-cancelable tokens and preserve the original test exception.</remarks>
    public async Task<TResult> RunInTestScenarioScopeAsync<TResult>(
        Func<AzureFunctionTestScenarioScope, CancellationToken, Task<TResult>> test,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);
        await using var scope = await CreateTestScenarioScopeAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await test(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await scope.CaptureFailureAsync(exception).ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<TestScenarioDiagnostics> CaptureDiagnosticsAsync(string scenarioId, TestFunctionContext? invocation = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, resource) in _resources)
        {
            try
            {
                values[name] = await resource.CaptureDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                values[name] = new { CaptureFailure = exception.ToString() };
            }
        }

        if (invocation is not null)
        {
            values["$Invocation"] = new
            {
                invocation.InvocationId,
                FunctionName = invocation.FunctionDefinition.Name,
                InputBindings = invocation.Bindings.Inputs.Keys.ToArray(),
                OutputBindings = invocation.Bindings.Outputs.Keys.ToArray()
            };
        }

        return new TestScenarioDiagnostics(scenarioId, DateTimeOffset.UtcNow, values);
    }

    internal TResource GetResource<TResource>(string name) where TResource : class, ITestScenarioResource
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_resources.TryGetValue(name, out var resource))
        {
            throw new KeyNotFoundException($"No scenario resource named '{name}' is registered.");
        }

        return resource as TResource ?? throw new InvalidOperationException($"The scenario resource '{name}' is not a {typeof(TResource).FullName}.");
    }

    internal async Task ResetResourcesAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var (name, resource) in _resources)
        {
            try
            {
                await resource.ResetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Preserve caller cancellation while still attempting every registered reset.
                failures ??= [];
                failures.Add(new InvalidOperationException($"Reset failed for scenario resource '{name}'.", exception));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (failures is not null)
        {
            throw new AggregateException("One or more scenario resources failed to reset.", failures);
        }
    }

    internal void ReleaseScenario()
    {
        _activeScenario = null;
        _scenarioGate.Release();
    }

    internal static void AddCleanupFailure(Exception failure, Exception cleanup)
    {
        var failures = new List<Exception>();
        if (failure.Data[CleanupExceptionDataKey] is AggregateException existing)
        {
            failures.AddRange(existing.InnerExceptions);
        }

        failures.Add(cleanup);
        failure.Data[CleanupExceptionDataKey] = new AggregateException("Scenario cleanup failed.", failures);
    }

    private void EnsureNoActiveScenario()
    {
        if (_activeScenario is not null)
        {
            throw new InvalidOperationException("Dispose the active Functions scenario before disposing its host.");
        }
    }
}
