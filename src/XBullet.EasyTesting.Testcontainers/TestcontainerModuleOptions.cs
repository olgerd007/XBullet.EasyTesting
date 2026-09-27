namespace XBullet.EasyTesting.Testcontainers;

/// <summary>Configures one of the built-in Testcontainers module registrations.</summary>
/// <typeparam name="TBuilder">The immutable native Testcontainers builder type to transform.</typeparam>
public sealed class TestcontainerModuleOptions<TBuilder>
    where TBuilder : class
{
    private Func<TBuilder, TBuilder>? _configureBuilder;

    /// <summary>Creates module options with package defaults.</summary>
    /// <param name="resourceName">The initial non-empty scenario resource name.</param>
    /// <param name="configurationKey">The initial non-empty application configuration key for the endpoint.</param>
    public TestcontainerModuleOptions(string resourceName, string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        ResourceName = resourceName;
        ConfigurationKey = configurationKey;
    }

    /// <summary>Gets or sets the scenario environment resource name.</summary>
    /// <value>
    /// The non-empty, case-insensitively unique resource name used for lookup and diagnostics. Module
    /// registration validates the final value after its configuration callback returns.
    /// </value>
    public string ResourceName { get; set; }

    /// <summary>Gets or sets the application configuration key that receives the endpoint.</summary>
    /// <value>
    /// The non-empty configuration key under which the module connection string or endpoint is
    /// published after readiness. Module registration validates the final value.
    /// </value>
    public string ConfigurationKey { get; set; }

    /// <summary>Adds an immutable native-builder transformation.</summary>
    /// <param name="configure">
    /// A non-null transformation retained until each scenario creates its container. Transformations
    /// run once per container in registration order and must return a non-null native builder.
    /// </param>
    /// <returns>This options instance, for chaining.</returns>
    public TestcontainerModuleOptions<TBuilder> ConfigureBuilder(
        Func<TBuilder, TBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var previous = _configureBuilder;
        _configureBuilder = previous is null
            ? configure
            : builder => configure(previous(builder));
        return this;
    }

    internal TBuilder Apply(TBuilder builder) =>
        _configureBuilder?.Invoke(builder)
        ?? builder;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConfigurationKey);
    }
}
