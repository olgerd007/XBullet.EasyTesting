using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Azure;

/// <summary>Registers deterministic Azure credentials and client replacements in test hosts.</summary>
public static class AzureTestingExtensions
{
    /// <summary>Replaces the application token credential for every test scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="credential">
    /// The non-null credential instance registered as the singleton <see cref="TokenCredential"/>.
    /// The instance is retained and shared; the caller owns it and any state it records.
    /// </param>
    /// <returns>The same host builder, for chaining.</returns>
    public static EasyTestHostBuilder<TEntryPoint> UseAzureTestCredential<TEntryPoint>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        TestTokenCredential credential)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(credential);
        return builder.ConfigureServices(services => ReplaceCredential(services, credential));
    }

    /// <summary>Replaces the application token credential for one test scenario.</summary>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="credential">
    /// The non-null credential instance registered as the scenario's singleton
    /// <see cref="TokenCredential"/>. The caller owns the retained instance and its recorded state.
    /// </param>
    /// <returns>The same scenario-scope builder, for chaining.</returns>
    public static TestScenarioScopeBuilder UseAzureTestCredential(
        this TestScenarioScopeBuilder builder,
        TestTokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(credential);
        return builder.ConfigureServices(services => ReplaceCredential(services, credential));
    }

    /// <summary>Replaces a directly injected Azure SDK client for every test scenario.</summary>
    /// <typeparam name="TEntryPoint">The application entry-point type configured by the host builder.</typeparam>
    /// <typeparam name="TClient">The reference type registered and resolved directly from dependency injection.</typeparam>
    /// <param name="builder">The non-null host builder to configure.</param>
    /// <param name="client">
    /// The non-null client instance registered as a singleton. The instance is retained and shared;
    /// because it was supplied externally, the caller remains responsible for disposing it when needed.
    /// </param>
    /// <returns>The same host builder, for chaining.</returns>
    public static EasyTestHostBuilder<TEntryPoint> ReplaceAzureClient<TEntryPoint, TClient>(
        this EasyTestHostBuilder<TEntryPoint> builder,
        TClient client)
        where TEntryPoint : class
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);
        return builder.ConfigureServices(services => ReplaceClient(services, client));
    }

    /// <summary>Replaces a directly injected Azure SDK client for one test scenario.</summary>
    /// <typeparam name="TClient">The reference type registered and resolved directly from dependency injection.</typeparam>
    /// <param name="builder">The non-null scenario-scope builder to configure.</param>
    /// <param name="client">
    /// The non-null client instance registered as a scenario singleton. The instance is retained;
    /// because it was supplied externally, the caller remains responsible for disposing it when needed.
    /// </param>
    /// <returns>The same scenario-scope builder, for chaining.</returns>
    public static TestScenarioScopeBuilder ReplaceAzureClient<TClient>(
        this TestScenarioScopeBuilder builder,
        TClient client)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);
        return builder.ConfigureServices(services => ReplaceClient(services, client));
    }

    private static void ReplaceCredential(
        IServiceCollection services,
        TestTokenCredential credential)
    {
        services.RemoveAll<TokenCredential>();
        services.AddSingleton<TokenCredential>(credential);
    }

    private static void ReplaceClient<TClient>(IServiceCollection services, TClient client)
        where TClient : class
    {
        services.RemoveAll<TClient>();
        services.AddSingleton(client);
    }
}
