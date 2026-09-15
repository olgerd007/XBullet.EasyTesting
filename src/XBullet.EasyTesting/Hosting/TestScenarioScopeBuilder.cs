using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Configures service and application-configuration overrides for one test scope.</summary>
public sealed class TestScenarioScopeBuilder
{
    private readonly List<Action<IConfigurationBuilder>> _configurationActions = [];
    private readonly List<Action<IServiceCollection>> _serviceActions = [];

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

    internal IReadOnlyList<Action<IConfigurationBuilder>> ConfigurationActions =>
        _configurationActions;

    internal IReadOnlyList<Action<IServiceCollection>> ServiceActions => _serviceActions;
}
