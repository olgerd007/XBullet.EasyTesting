using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

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
    /// <typeparam name="TFunction">The reference-type function class registered with transient lifetime.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public AzureFunctionTestHostBuilder AddFunction<TFunction>()
        where TFunction : class
    {
        _services.AddTransient<TFunction>();
        return this;
    }

    /// <summary>Configures dependencies used by function instances and invocation contexts.</summary>
    /// <param name="configure">
    /// A non-null callback invoked synchronously once with the builder-owned mutable service collection.
    /// It may add, remove, or replace registrations but must not retain the collection.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public AzureFunctionTestHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
        return this;
    }

    /// <summary>Adds isolated-worker middleware to the test invocation pipeline.</summary>
    /// <typeparam name="TMiddleware">
    /// The middleware class registered transiently and resolved once per invocation from its scope.
    /// </typeparam>
    /// <returns>This builder, for chaining. Middleware executes in registration order.</returns>
    public AzureFunctionTestHostBuilder UseMiddleware<TMiddleware>()
        where TMiddleware : class, IFunctionsWorkerMiddleware
    {
        _services.AddTransient<TMiddleware>();
        _middleware.Add(provider => provider.GetRequiredService<TMiddleware>());
        return this;
    }

    /// <summary>Adds inline middleware to the test invocation pipeline.</summary>
    /// <param name="middleware">
    /// A non-null asynchronous delegate retained by the host and invoked once per invocation in
    /// registration order. It may short-circuit by not invoking the supplied next delegate.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public AzureFunctionTestHostBuilder UseMiddleware(
        Func<FunctionContext, FunctionExecutionDelegate, Task> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middleware.Add(_ => new DelegateFunctionsWorkerMiddleware(middleware));
        return this;
    }

    /// <summary>Creates the configured function test host.</summary>
    /// <returns>
    /// A new host that owns its service provider and must be disposed. Registered singleton and
    /// scoped disposable services are released with the host or invocation scope respectively.
    /// </returns>
    public AzureFunctionTestHost Build() =>
        new(_services.BuildServiceProvider(), _middleware);
}
