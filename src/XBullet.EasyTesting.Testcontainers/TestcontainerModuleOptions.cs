namespace XBullet.EasyTesting.Testcontainers;

/// <summary>Configures one of the built-in Testcontainers module registrations.</summary>
public sealed class TestcontainerModuleOptions<TBuilder>
    where TBuilder : class
{
    private Func<TBuilder, TBuilder>? _configureBuilder;

    /// <summary>Creates module options with package defaults.</summary>
    public TestcontainerModuleOptions(string resourceName, string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        ResourceName = resourceName;
        ConfigurationKey = configurationKey;
    }

    /// <summary>Gets or sets the scenario environment resource name.</summary>
    public string ResourceName { get; set; }

    /// <summary>Gets or sets the application configuration key that receives the endpoint.</summary>
    public string ConfigurationKey { get; set; }

    /// <summary>Adds an immutable native-builder transformation.</summary>
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
