using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Authentication;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// Composes configuration, services, and authentication before creating an integration-test host.
/// </summary>
/// <typeparam name="TEntryPoint">
/// The application entry-point type used to locate and bootstrap the ASP.NET Core application.
/// </typeparam>
public sealed class EasyTestHostBuilder<TEntryPoint>
    where TEntryPoint : class
{
    private readonly List<Action<IConfigurationBuilder>> _configurationActions = [];
    private readonly List<Action<IServiceCollection>> _serviceActions = [];
    private readonly List<Action<TestAuthenticationSchemeBuilder>> _authenticationActions = [];
    private readonly List<Action<TestScenarioEnvironmentBuilder>> _environmentActions = [];
    private readonly List<Action<IDictionary<string, string>>> _hostSettingActions = [];
    private bool _built;

    /// <summary>Adds an application-configuration action and returns this builder.</summary>
    /// <param name="configure">
    /// The callback invoked whenever the application factory constructs a host. It receives the
    /// host-owned configuration builder and may add or replace configuration sources. Registered
    /// callbacks run sequentially in registration order, but can run concurrently when callers
    /// construct derived factories in parallel.
    /// </param>
    /// <returns>This builder so additional host behavior can be configured.</returns>
    public EasyTestHostBuilder<TEntryPoint> ConfigureConfiguration(
        Action<IConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _configurationActions.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds settings that are visible to minimal-hosting startup code as soon as
    /// <c>WebApplication.CreateBuilder</c> returns.
    /// </summary>
    /// <param name="configure">
    /// The callback invoked before minimal-hosting startup code runs for each constructed host. It
    /// receives a mutable, case-insensitive settings dictionary and may add or replace values.
    /// Registered callbacks run sequentially in registration order, but can run concurrently when
    /// callers construct derived factories in parallel.
    /// </param>
    /// <returns>This builder so additional host behavior can be configured.</returns>
    public EasyTestHostBuilder<TEntryPoint> ConfigureHostSettings(
        Action<IDictionary<string, string>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _hostSettingActions.Add(configure);
        return this;
    }

    /// <summary>Adds or replaces one early host setting.</summary>
    /// <param name="key">The non-empty host-setting key to add or replace.</param>
    /// <param name="value">
    /// The setting value. Empty strings are accepted; <see langword="null"/> is not.
    /// </param>
    /// <returns>This builder so additional host behavior can be configured.</returns>
    public EasyTestHostBuilder<TEntryPoint> UseSetting(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        return ConfigureHostSettings(settings => settings[key] = value);
    }

    /// <summary>Adds a test-service configuration action and returns this builder.</summary>
    /// <param name="configure">
    /// The callback invoked for each host after XBullet registers test authentication. It receives the
    /// host-owned service collection and may add, replace, or remove registrations. Registered
    /// callbacks run sequentially in registration order, but can run concurrently when callers
    /// construct derived factories in parallel.
    /// </param>
    /// <returns>This builder so additional host behavior can be configured.</returns>
    public EasyTestHostBuilder<TEntryPoint> ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _serviceActions.Add(configure);
        return this;
    }

    /// <summary>Adds a test-authentication configuration action and returns this builder.</summary>
    /// <param name="configure">
    /// The callback invoked once before the test host is constructed. It receives the mutable
    /// authentication builder used to select simulated and end-to-end schemes. Registered callbacks
    /// run sequentially in registration order.
    /// </param>
    /// <returns>This builder so additional host behavior can be configured.</returns>
    public EasyTestHostBuilder<TEntryPoint> ConfigureAuthentication(
        Action<TestAuthenticationSchemeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _authenticationActions.Add(configure);
        return this;
    }

    /// <summary>Adds external dependencies that are created for every test scenario.</summary>
    /// <param name="configure">
    /// The callback invoked once for each scenario before its external resources are created. It
    /// receives that scenario's environment builder. Registered callbacks run sequentially in
    /// registration order and are not invoked concurrently by the built factory.
    /// </param>
    /// <returns>This builder so additional host behavior can be configured.</returns>
    public EasyTestHostBuilder<TEntryPoint> ConfigureEnvironment(
        Action<TestScenarioEnvironmentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotBuilt();
        _environmentActions.Add(configure);
        return this;
    }

    /// <summary>Creates the configured application factory. A builder can be built only once.</summary>
    /// <returns>
    /// A new application factory owned by the caller. Dispose the factory after all clients and
    /// scenario scopes created from it have been disposed.
    /// </returns>
    /// <exception cref="InvalidOperationException">This builder has already been built.</exception>
    public AuthenticatedWebApplicationFactory<TEntryPoint> Build()
    {
        EnsureNotBuilt();
        _built = true;
        var serviceActions = _serviceActions.ToArray();
        var configurationActions = _configurationActions.ToArray();
        var authenticationActions = _authenticationActions.ToArray();
        var environmentActions = _environmentActions.ToArray();
        var hostSettingActions = _hostSettingActions.ToArray();

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
            },
            settings =>
            {
                foreach (var configure in hostSettingActions)
                {
                    configure(settings);
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
