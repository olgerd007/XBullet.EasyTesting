namespace XBullet.EasyTesting.Hosting;

/// <summary>Provides the scenario identifier and cleanup registration to host extensions.</summary>
public sealed class TestScenarioContext
{
    private readonly List<Func<ValueTask>> _cleanupActions = [];

    internal TestScenarioContext(string scenarioId)
    {
        ScenarioId = scenarioId;
    }

    /// <summary>Gets the unique identifier for this scenario scope.</summary>
    public string ScenarioId { get; }

    /// <summary>Registers an asynchronously disposable scenario-owned resource.</summary>
    public void DisposeWithScenario(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _cleanupActions.Add(resource.DisposeAsync);
    }

    /// <summary>Registers a disposable scenario-owned resource.</summary>
    public void DisposeWithScenario(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _cleanupActions.Add(() =>
        {
            resource.Dispose();
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Registers a custom scenario cleanup action.</summary>
    public void OnCleanup(Func<ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _cleanupActions.Add(cleanup);
    }

    internal async ValueTask CleanupAsync()
    {
        List<Exception>? failures = null;
        for (var index = _cleanupActions.Count - 1; index >= 0; index--)
        {
            try
            {
                await _cleanupActions[index]();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more scenario resources failed to clean up.", failures);
        }
    }
}
