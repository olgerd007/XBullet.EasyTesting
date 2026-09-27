using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Observability;

/// <summary>Adds observability capture to integration-test scenarios.</summary>
public static class TestObservabilityExtensions
{
    /// <summary>Adds a fresh observability resource to every scenario created by the host.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <returns>
    /// The same builder, for chaining. Each scenario receives a resource named <c>Observability</c>
    /// with default limits, system time, and no source or meter filters.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseObservability<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder)
        where TEntryPoint : class =>
        UseObservability(builder, "Observability", null);

    /// <summary>Adds configured observability to every scenario created by the host.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback retained and invoked once for each scenario with new mutable options. It
    /// must not retain the options beyond invocation.
    /// </param>
    /// <returns>The same builder, for chaining, using resource name <c>Observability</c>.</returns>
    public static EasyTestHostBuilder<TEntryPoint> UseObservability<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        Action<TestObservabilityOptions> configure)
        where TEntryPoint : class =>
        UseObservability(builder, "Observability", configure);

    /// <summary>Adds named observability to every scenario created by the host.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="resourceName">
    /// The non-empty, case-insensitively unique scenario resource name used for lookup and diagnostics.
    /// </param>
    /// <param name="configure">
    /// An optional callback retained and invoked once for each scenario with new mutable options, or
    /// <see langword="null"/> to use defaults. It must not retain the options beyond invocation.
    /// </param>
    /// <returns>The same builder, for chaining. Each scenario owns and disposes its resource.</returns>
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
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <returns>
    /// The same builder, for chaining, with a resource named <c>Observability</c>, default limits,
    /// system time, and no source or meter filters.
    /// </returns>
    public static TestScenarioScopeBuilder UseObservability(
        this TestScenarioScopeBuilder builder) =>
        UseObservability(builder, "Observability", null);

    /// <summary>Adds configured observability to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="configure">
    /// A non-null callback retained until scope creation, then invoked once with new mutable options.
    /// It must not retain the options beyond invocation.
    /// </param>
    /// <returns>The same builder, for chaining, using resource name <c>Observability</c>.</returns>
    public static TestScenarioScopeBuilder UseObservability(
        this TestScenarioScopeBuilder builder,
        Action<TestObservabilityOptions> configure) =>
        UseObservability(builder, "Observability", configure);

    /// <summary>Adds named observability to one scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="resourceName">
    /// The non-empty, case-insensitively unique resource name used for lookup and diagnostics.
    /// </param>
    /// <param name="configure">
    /// An optional callback retained until scope creation, then invoked once with new mutable options,
    /// or <see langword="null"/> to use defaults. It must not retain the options beyond invocation.
    /// </param>
    /// <returns>The same builder, for chaining. The scenario owns and disposes the resource.</returns>
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
    /// <typeparam name="TEntryPoint">The application entry-point type hosted by the scenario scope.</typeparam>
    /// <param name="scope">The non-null, active scenario scope containing the resource.</param>
    /// <param name="resourceName">
    /// The non-empty registered resource name, matched case-insensitively. The default is
    /// <c>Observability</c>.
    /// </param>
    /// <returns>
    /// The scenario-owned observability resource. The caller may use it while the scope is active but
    /// must not dispose or retain it beyond scope disposal.
    /// </returns>
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
