namespace XBullet.EasyTesting.Hosting;

/// <summary>Provides the scenario identifier and cleanup registration to host extensions.</summary>
public sealed class TestScenarioContext
{
    private readonly List<Func<ValueTask>> _cleanupActions = [];
    private readonly List<TestScenarioEnvironmentResourceRegistration> _environmentResources = [];

    internal TestScenarioContext(string scenarioId)
    {
        ScenarioId = scenarioId;
    }

    /// <summary>Gets the unique identifier for this scenario scope.</summary>
    public string ScenarioId { get; }

    internal IReadOnlyList<TestScenarioEnvironmentResourceRegistration> EnvironmentResources =>
        _environmentResources;

    internal void AddEnvironmentResource(
        string name,
        ITestScenarioEnvironmentResource resource)
    {
        _environmentResources.Add(new TestScenarioEnvironmentResourceRegistration(name, resource));
        DisposeWithScenario(resource);
    }

    internal TResource GetEnvironmentResource<TResource>(string name)
        where TResource : class, ITestScenarioEnvironmentResource
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var registration = _environmentResources.SingleOrDefault(resource =>
            string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase));
        if (registration is null)
        {
            throw new KeyNotFoundException(
                $"No test scenario environment resource named '{name}' is registered.");
        }

        if (registration.Resource is not TResource typedResource)
        {
            throw new InvalidOperationException(
                $"The test scenario environment resource '{name}' is not a {typeof(TResource).FullName}.");
        }

        return typedResource;
    }

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

internal sealed record TestScenarioEnvironmentResourceRegistration(
    string Name,
    ITestScenarioEnvironmentResource Resource);
