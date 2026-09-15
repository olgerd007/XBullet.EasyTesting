using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluently builds a dependency-injection host for isolated-worker function tests.</summary>
public sealed class AzureFunctionTestHostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly List<Func<IServiceProvider, IFunctionsWorkerMiddleware>> _middleware = [];

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

    /// <summary>Adds isolated-worker middleware to the test invocation pipeline.</summary>
    public AzureFunctionTestHostBuilder UseMiddleware<TMiddleware>()
        where TMiddleware : class, IFunctionsWorkerMiddleware
    {
        _services.AddTransient<TMiddleware>();
        _middleware.Add(provider => provider.GetRequiredService<TMiddleware>());
        return this;
    }

    /// <summary>Adds inline middleware to the test invocation pipeline.</summary>
    public AzureFunctionTestHostBuilder UseMiddleware(
        Func<FunctionContext, FunctionExecutionDelegate, Task> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middleware.Add(_ => new DelegateFunctionsWorkerMiddleware(middleware));
        return this;
    }

    /// <summary>Creates the configured function test host.</summary>
    public AzureFunctionTestHost Build() =>
        new(_services.BuildServiceProvider(), _middleware);
}
