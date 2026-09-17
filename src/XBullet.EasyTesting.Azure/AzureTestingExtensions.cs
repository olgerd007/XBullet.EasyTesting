using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Azure;

/// <summary>Registers deterministic Azure credentials and client replacements in test hosts.</summary>
public static class AzureTestingExtensions
{
    /// <summary>Replaces the application token credential for every test scenario.</summary>
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
    public static TestScenarioScopeBuilder UseAzureTestCredential(
        this TestScenarioScopeBuilder builder,
        TestTokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(credential);
        return builder.ConfigureServices(services => ReplaceCredential(services, credential));
    }

    /// <summary>Replaces a directly injected Azure SDK client for every test scenario.</summary>
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
