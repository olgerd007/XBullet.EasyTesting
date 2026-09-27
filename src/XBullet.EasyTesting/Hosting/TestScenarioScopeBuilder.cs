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
    /// <param name="configure">
    /// The callback invoked during scenario host construction after environment resources have
    /// contributed their configuration. It receives the caller-owned configuration builder, runs
    /// once for this scenario, and is not invoked concurrently by a single test factory.
    /// </param>
    /// <returns>This builder so additional scenario overrides can be registered.</returns>
    public TestScenarioScopeBuilder ConfigureConfiguration(
        Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configurationActions.Add(configure);
        return this;
    }

    /// <summary>Adds a service override that exists only for this scenario.</summary>
    /// <param name="configure">
    /// The callback invoked during scenario host construction after environment resources and the
    /// factory have registered their services. It receives the caller-owned service collection,
    /// runs once for this scenario, and is not invoked concurrently by a single test factory.
    /// </param>
    /// <returns>This builder so additional scenario overrides can be registered.</returns>
    public TestScenarioScopeBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _serviceActions.Add(configure);
        return this;
    }

    /// <summary>Adds an external dependency owned by this scenario.</summary>
    /// <param name="name">
    /// The non-empty, case-insensitively unique name used to retrieve the resource from the scope.
    /// </param>
    /// <param name="resource">
    /// The resource whose ownership transfers to this scenario. It is started before the test host
    /// and asynchronously disposed during scenario cleanup.
    /// </param>
    /// <returns>This builder so additional scenario overrides can be registered.</returns>
    public TestScenarioScopeBuilder UseEnvironmentResource(
        string name,
        ITestScenarioEnvironmentResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _environment.AddResource(name, _ => resource);
        return this;
    }

    /// <summary>Adds an external dependency factory owned by this scenario.</summary>
    /// <param name="name">
    /// The non-empty, case-insensitively unique name used to retrieve the resource from the scope.
    /// </param>
    /// <param name="createResource">
    /// The factory invoked once while this scenario is being created. It receives the scenario
    /// context and must return a non-null resource. The scenario assumes ownership of the returned
    /// resource and disposes it during cleanup.
    /// </param>
    /// <returns>This builder so additional scenario overrides can be registered.</returns>
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
