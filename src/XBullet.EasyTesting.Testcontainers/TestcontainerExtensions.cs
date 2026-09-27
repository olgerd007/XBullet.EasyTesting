using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Testcontainers;

/// <summary>Registers arbitrary Testcontainers containers with EasyTesting scenarios.</summary>
public static class TestcontainerExtensions
{
    /// <summary>Adds a container that is created for every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <typeparam name="TContainer">The concrete Testcontainers container type created for each scenario.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="resourceName">
    /// The non-empty, case-insensitively unique scenario resource name used for diagnostics and lookup.
    /// </param>
    /// <param name="createContainer">
    /// A non-null factory invoked once per scenario with its context. It must return a non-null,
    /// unstarted container whose ownership transfers to the scenario.
    /// </param>
    /// <returns>
    /// The same host builder, for chaining. Each container uses no published configuration or service
    /// callback and retains at most 20,000 characters from each diagnostic output stream.
    /// </returns>
    public static EasyTestHostBuilder<TEntryPoint> UseTestcontainer<TEntryPoint, TContainer>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        string resourceName,
        Func<TestScenarioContext, TContainer> createContainer)
        where TEntryPoint : class
        where TContainer : class, IContainer =>
        builder.UseTestcontainer(
            resourceName,
            createContainer,
            configurationValues: null,
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);

    /// <summary>Adds a container that is created for every scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <typeparam name="TContainer">The concrete Testcontainers container type created for each scenario.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="resourceName">
    /// The non-empty, case-insensitively unique scenario resource name used for diagnostics and lookup.
    /// </param>
    /// <param name="createContainer">
    /// A non-null factory invoked once per scenario with its context. It must return a non-null,
    /// unstarted container whose ownership transfers to the scenario.
    /// </param>
    /// <param name="configurationValues">
    /// An optional callback invoked after the container is ready to obtain application configuration.
    /// It must return a non-null dictionary with non-empty keys; null values are accepted. Published
    /// values are excluded from scenario diagnostics. The callback must not dispose the container.
    /// </param>
    /// <param name="configureServices">
    /// An optional callback invoked after readiness and after the container is registered as a
    /// singleton. It may update the scenario service collection in place but must not retain the
    /// collection or dispose the scenario-owned container.
    /// </param>
    /// <param name="maximumDiagnosticCharacters">
    /// The non-negative maximum number of trailing characters retained from each of standard output
    /// and standard error. Zero retains empty log strings.
    /// </param>
    /// <returns>The same host builder, for chaining.</returns>
    public static EasyTestHostBuilder<TEntryPoint> UseTestcontainer<TEntryPoint, TContainer>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        string resourceName,
        Func<TestScenarioContext, TContainer> createContainer,
        Func<TContainer, IReadOnlyDictionary<string, string?>>? configurationValues,
        Action<TContainer, IServiceCollection>? configureServices,
        int maximumDiagnosticCharacters)
        where TEntryPoint : class
        where TContainer : class, IContainer
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentNullException.ThrowIfNull(createContainer);
        return builder.ConfigureEnvironment(environment => environment.AddResource(
            resourceName,
            context => new TestcontainerResource<TContainer>(
                createContainer(context),
                configurationValues,
                configureServices,
                maximumDiagnosticCharacters)));
    }

    /// <summary>Adds a container to one scenario scope.</summary>
    /// <typeparam name="TContainer">The concrete Testcontainers container type created for the scenario.</typeparam>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="resourceName">
    /// The non-empty, case-insensitively unique scenario resource name used for diagnostics and lookup.
    /// </param>
    /// <param name="createContainer">
    /// A non-null factory invoked once with the scenario context. It must return a non-null, unstarted
    /// container whose ownership transfers to the scenario.
    /// </param>
    /// <returns>
    /// The same scope builder, for chaining. The container uses no published configuration or service
    /// callback and retains at most 20,000 characters from each diagnostic output stream.
    /// </returns>
    public static TestScenarioScopeBuilder UseTestcontainer<TContainer>(
        this TestScenarioScopeBuilder builder,
        string resourceName,
        Func<TestScenarioContext, TContainer> createContainer)
        where TContainer : class, IContainer =>
        builder.UseTestcontainer(
            resourceName,
            createContainer,
            configurationValues: null,
            configureServices: null,
            maximumDiagnosticCharacters: 20_000);

    /// <summary>Adds a container to one scenario scope.</summary>
    /// <typeparam name="TContainer">The concrete Testcontainers container type created for the scenario.</typeparam>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="resourceName">
    /// The non-empty, case-insensitively unique scenario resource name used for diagnostics and lookup.
    /// </param>
    /// <param name="createContainer">
    /// A non-null factory invoked once with the scenario context. It must return a non-null, unstarted
    /// container whose ownership transfers to the scenario.
    /// </param>
    /// <param name="configurationValues">
    /// An optional callback invoked after readiness to obtain application configuration. It must
    /// return a non-null dictionary with non-empty keys; null values are accepted. Published values
    /// are excluded from diagnostics. The callback must not dispose the container.
    /// </param>
    /// <param name="configureServices">
    /// An optional callback invoked after readiness and singleton registration. It may update the
    /// service collection in place but must not retain it or dispose the scenario-owned container.
    /// </param>
    /// <param name="maximumDiagnosticCharacters">
    /// The non-negative maximum number of trailing characters retained from each of standard output
    /// and standard error. Zero retains empty log strings.
    /// </param>
    /// <returns>The same scope builder, for chaining.</returns>
    public static TestScenarioScopeBuilder UseTestcontainer<TContainer>(
        this TestScenarioScopeBuilder builder,
        string resourceName,
        Func<TestScenarioContext, TContainer> createContainer,
        Func<TContainer, IReadOnlyDictionary<string, string?>>? configurationValues,
        Action<TContainer, IServiceCollection>? configureServices,
        int maximumDiagnosticCharacters)
        where TContainer : class, IContainer
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentNullException.ThrowIfNull(createContainer);
        return builder.UseEnvironmentResource(
            resourceName,
            context => new TestcontainerResource<TContainer>(
                createContainer(context),
                configurationValues,
                configureServices,
                maximumDiagnosticCharacters));
    }

    /// <summary>Gets the native container registered under a scenario resource name.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type hosted by the scenario scope.</typeparam>
    /// <typeparam name="TContainer">The expected concrete container type.</typeparam>
    /// <param name="scope">The non-null, active scenario scope containing the resource.</param>
    /// <param name="resourceName">The non-empty registered resource name. Matching is case-insensitive.</param>
    /// <returns>
    /// The started, scenario-owned native container. The caller may use it while the scope is active
    /// but must not dispose or retain it beyond scope disposal.
    /// </returns>
    public static TContainer GetTestcontainer<TEntryPoint, TContainer>(
        this TestScenarioScope<TEntryPoint> scope,
        string resourceName)
        where TEntryPoint : class
        where TContainer : class, IContainer
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope
            .GetEnvironmentResource<TestcontainerResource<TContainer>>(resourceName)
            .Container;
    }
}
