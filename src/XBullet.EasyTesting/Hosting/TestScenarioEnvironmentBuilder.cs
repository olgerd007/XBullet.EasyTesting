namespace XBullet.EasyTesting.Hosting;

/// <summary>Defines external dependencies that are created for each test scenario.</summary>
public sealed class TestScenarioEnvironmentBuilder
{
    private readonly List<TestScenarioEnvironmentResourceDefinition> _resources = [];

    /// <summary>Adds an external dependency factory and returns this builder.</summary>
    public TestScenarioEnvironmentBuilder AddResource(
        string name,
        Func<TestScenarioContext, ITestScenarioEnvironmentResource> createResource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(createResource);

        if (_resources.Any(resource =>
            string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"A test scenario environment resource named '{name}' is already registered.");
        }

        _resources.Add(new TestScenarioEnvironmentResourceDefinition(name, createResource));
        return this;
    }

    internal IReadOnlyList<TestScenarioEnvironmentResourceDefinition> Resources => _resources;
}

internal sealed record TestScenarioEnvironmentResourceDefinition(
    string Name,
    Func<TestScenarioContext, ITestScenarioEnvironmentResource> CreateResource);
