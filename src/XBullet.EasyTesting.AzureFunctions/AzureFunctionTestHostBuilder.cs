using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluently builds a dependency-injection host for isolated-worker function tests.</summary>
public sealed class AzureFunctionTestHostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();

    internal AzureFunctionTestHostBuilder()
    {
        _services.AddLogging();
        _services.Configure<WorkerOptions>(options =>
            options.Serializer = new JsonObjectSerializer(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    /// <summary>Adds a function class that can be resolved from the test host.</summary>
    public AzureFunctionTestHostBuilder AddFunction<TFunction>()
        where TFunction : class
    {
        _services.AddTransient<TFunction>();
        return this;
    }

    /// <summary>Configures dependencies used by function instances and invocation contexts.</summary>
    public AzureFunctionTestHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
        return this;
    }

    /// <summary>Creates the configured function test host.</summary>
    public AzureFunctionTestHost Build() => new(_services.BuildServiceProvider());
}
