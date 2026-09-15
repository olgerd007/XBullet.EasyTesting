using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Configures service and application-configuration overrides for one test scope.</summary>
public sealed class TestScenarioScopeBuilder
{
    private readonly List<Action<IConfigurationBuilder>> _configurationActions = [];
    private readonly List<Action<IServiceCollection>> _serviceActions = [];
    private readonly TestScenarioEnvironmentBuilder _environment = new();

    /// <summary>Adds a configuration override that exists only for this scenario.</summary>
    public TestScenarioScopeBuilder ConfigureConfiguration(
        Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configurationActions.Add(configure);
        return this;
    }

    /// <summary>Adds a service override that exists only for this scenario.</summary>
    public TestScenarioScopeBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _serviceActions.Add(configure);
        return this;
    }

    /// <summary>Adds an external dependency owned by this scenario.</summary>
    public TestScenarioScopeBuilder UseEnvironmentResource(
        string name,
        ITestScenarioEnvironmentResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _environment.AddResource(name, _ => resource);
        return this;
    }

    /// <summary>Adds an external dependency factory owned by this scenario.</summary>
    public TestScenarioScopeBuilder UseEnvironmentResource(
        string name,
        Func<TestScenarioContext, ITestScenarioEnvironmentResource> createResource)
    {
        _environment.AddResource(name, createResource);
        return this;
    }

    internal IReadOnlyList<Action<IConfigurationBuilder>> ConfigurationActions =>
        _configurationActions;

    internal IReadOnlyList<Action<IServiceCollection>> ServiceActions => _serviceActions;

    internal TestScenarioEnvironmentBuilder Environment => _environment;
}
