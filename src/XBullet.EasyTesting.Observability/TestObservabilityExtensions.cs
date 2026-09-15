using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Observability;

/// <summary>Adds observability capture to integration-test scenarios.</summary>
public static class TestObservabilityExtensions
{
    /// <summary>Adds a fresh observability resource to every scenario created by the host.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseObservability<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        UseObservability(builder, "Observability", null);

    /// <summary>Adds configured observability to every scenario created by the host.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseObservability<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestObservabilityOptions> configure)
        where TEntryPoint : class =>
        UseObservability(builder, "Observability", configure);

    /// <summary>Adds named observability to every scenario created by the host.</summary>
    public static EasyTestHostBuilder<TEntryPoint> UseObservability<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        string resourceName,
        Action<TestObservabilityOptions>? configure)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return builder.ConfigureEnvironment(environment => environment.AddResource(
            resourceName,
            _ => Create(configure)));
    }

    /// <summary>Adds a fresh observability resource to one scenario.</summary>
    public static TestScenarioScopeBuilder UseObservability(
        this TestScenarioScopeBuilder builder) =>
        UseObservability(builder, "Observability", null);

    /// <summary>Adds configured observability to one scenario.</summary>
    public static TestScenarioScopeBuilder UseObservability(
        this TestScenarioScopeBuilder builder,
        Action<TestObservabilityOptions> configure) =>
        UseObservability(builder, "Observability", configure);

    /// <summary>Adds named observability to one scenario.</summary>
    public static TestScenarioScopeBuilder UseObservability(
        this TestScenarioScopeBuilder builder,
        string resourceName,
        Action<TestObservabilityOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return builder.UseEnvironmentResource(resourceName, _ => Create(configure));
    }

    /// <summary>Gets the named observability resource from a scenario.</summary>
    public static TestObservability GetObservability<TEntryPoint>(
        this TestScenarioScope<TEntryPoint> scope,
        string resourceName = "Observability")
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.GetEnvironmentResource<TestObservability>(resourceName);
    }

    private static TestObservability Create(Action<TestObservabilityOptions>? configure)
    {
        var options = new TestObservabilityOptions();
        configure?.Invoke(options);
        return new TestObservability(options);
    }
}
