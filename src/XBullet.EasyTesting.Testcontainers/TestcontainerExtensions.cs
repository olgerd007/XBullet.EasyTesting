using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Testcontainers;

/// <summary>Registers arbitrary Testcontainers containers with EasyTesting scenarios.</summary>
public static class TestcontainerExtensions
{
    /// <summary>Adds a container that is created for every scenario.</summary>
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
