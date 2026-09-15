using XBullet.EasyTesting.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// Composes configuration, services, and authentication before creating an integration-test host.
/// </summary>
public sealed class EasyTestHostBuilder<TEntryPoint>
    where TEntryPoint : class
{
    private readonly List<Action<IConfigurationBuilder>> _configurationActions = [];
    private readonly List<Action<IServiceCollection>> _serviceActions = [];
    private readonly List<Action<TestAuthenticationSchemeBuilder>> _authenticationActions = [];
    private readonly List<Action<TestScenarioEnvironmentBuilder>> _environmentActions = [];
    private bool _built;

    /// <summary>Adds an application-configuration action and returns this builder.</summary>
    public EasyTestHostBuilder<TEntryPoint> ConfigureConfiguration(
        Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _configurationActions.Add(configure);
        return this;
    }

    /// <summary>Adds a test-service configuration action and returns this builder.</summary>
    public EasyTestHostBuilder<TEntryPoint> ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _serviceActions.Add(configure);
        return this;
    }

    /// <summary>Adds a test-authentication configuration action and returns this builder.</summary>
    public EasyTestHostBuilder<TEntryPoint> ConfigureAuthentication(
        Action<TestAuthenticationSchemeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _authenticationActions.Add(configure);
        return this;
    }

    /// <summary>Adds external dependencies that are created for every test scenario.</summary>
    public EasyTestHostBuilder<TEntryPoint> ConfigureEnvironment(
        Action<TestScenarioEnvironmentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _environmentActions.Add(configure);
        return this;
    }

    /// <summary>Creates the configured application factory. A builder can be built only once.</summary>
    public AuthenticatedWebApplicationFactory<TEntryPoint> Build()
    {
        EnsureNotBuilt();
        _built = true;
        var serviceActions = _serviceActions.ToArray();
        var configurationActions = _configurationActions.ToArray();
        var authenticationActions = _authenticationActions.ToArray();
        var environmentActions = _environmentActions.ToArray();

        return AuthenticatedWebApplicationFactory<TEntryPoint>.CreateWithEnvironment(
            services =>
            {
                foreach (var configure in serviceActions)
                {
                    configure(services);
                }
            },
            configuration =>
            {
                foreach (var configure in configurationActions)
                {
                    configure(configuration);
                }
            },
            authentication =>
            {
                foreach (var configure in authenticationActions)
                {
                    configure(authentication);
                }
            },
            environment =>
            {
                foreach (var configure in environmentActions)
                {
                    configure(environment);
                }
            });
    }

    private void EnsureNotBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException("The easy test host builder has already been built.");
        }
    }
}
