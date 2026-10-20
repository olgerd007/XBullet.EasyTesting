using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Reuses an isolated Functions application's entry point and container for in-process tests.</summary>
/// <typeparam name="TEntryPoint">A type in the executable Functions assembly, commonly its public partial Program class.</typeparam>
/// <remarks>
/// The factory owns one lazily created test host. Program runs until its first generic host is built;
/// hosted services and worker transport are never started by this factory. Register test invocation
/// middleware and scenario resources through <see cref="ConfigureTestHost"/>. Dispose active scenarios
/// before disposing this factory. Startup code preceding host construction still executes normally.
/// </remarks>
public class AzureFunctionTestFactory<TEntryPoint> : IAsyncDisposable where TEntryPoint : class
{
    private readonly SemaphoreSlim _lifetimeGate = new(1, 1);
    private AzureFunctionTestHost? _host;
    private bool _disposed;

    /// <summary>Creates a factory that lazily runs the Functions entry point.</summary>
    public AzureFunctionTestFactory() { }

    /// <summary>Gets the maximum wait for Program to build its host.</summary>
    /// <value>A positive finite timer duration, defaulting to thirty seconds.</value>
    /// <remarks>Cancellation or timeout cannot interrupt application code; a late-built host is stopped and disposed.</remarks>
    protected virtual TimeSpan StartupTimeout => TimeSpan.FromSeconds(30);

    /// <summary>Supplies command-line arguments passed to Program.</summary>
    /// <returns>A non-null array with non-null elements, defaulting to an empty array. It is copied before execution.</returns>
    protected virtual string[] GetArguments() => [];

    /// <summary>Applies test configuration to the production host before it builds services.</summary>
    /// <param name="builder">The borrowed generic host builder adapter. Do not build or retain it.</param>
    /// <remarks>Called during HostBuilding. HostApplicationBuilder may reject changes to already-initialized host settings.</remarks>
    protected virtual void ConfigureHost(IHostBuilder builder) { }

    /// <summary>Configures function registrations, test invocation middleware, and scenario resources.</summary>
    /// <param name="builder">The borrowed builder over the production service collection. Do not build or retain it.</param>
    /// <remarks>Production function classes are discovered automatically. Worker-native middleware is not replayed automatically.</remarks>
    protected virtual void ConfigureTestHost(AzureFunctionTestHostBuilder builder) { }

    /// <summary>Applies final dependency overrides after production and test-host registrations.</summary>
    /// <param name="services">The borrowed production service collection. Do not build a separate provider or retain it.</param>
    protected virtual void ConfigureTestServices(IServiceCollection services) { }

    /// <summary>Gets or constructs the factory-owned function test host.</summary>
    /// <param name="cancellationToken">Cancels gate acquisition and the startup wait.</param>
    /// <returns>The borrowed, cached host. Dispose its scenarios before disposing the factory; do not dispose this host separately.</returns>
    /// <remarks>
    /// Calls are serialized. Completed initialization failures can be retried. Application code
    /// continuing after a canceled startup wait can outlive that wait and its configuration callbacks.
    /// </remarks>
    public async Task<AzureFunctionTestHost> CreateHostAsync(CancellationToken cancellationToken = default)
    {
        await _lifetimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_host is not null)
            {
                return _host;
            }

            var assembly = typeof(TEntryPoint).Assembly;
            var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' has no entry point. Use a type from the executable Functions application.");
            var arguments = GetArguments();
            ArgumentNullException.ThrowIfNull(arguments);
            if (arguments.Any(argument => argument is null))
            {
                throw new InvalidOperationException("Functions entry-point arguments must not contain null elements.");
            }

            arguments = arguments.ToArray();
            AzureFunctionTestHostBuilder? testBuilder = null;
            var application = await FunctionProgramHostCapture.CaptureAsync(
                () => InvokeEntryPointAsync(entryPoint, arguments),
                builder =>
                {
                    ConfigureHost(builder);
                    builder.ConfigureServices((_, services) =>
                    {
                        RegisterFunctions(services, assembly);
                        // The worker logger depends on a running RPC channel. Other application
                        // logging providers stay in place, and tests can add their own provider.
                        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider) &&
                            descriptor.ImplementationType?.FullName == "Microsoft.Azure.Functions.Worker.Logging.WorkerLoggerProvider").ToArray())
                        {
                            services.Remove(descriptor);
                        }
                        testBuilder = new AzureFunctionTestHostBuilder(services);
                        ConfigureTestHost(testBuilder);
                        ConfigureTestServices(services);
                    });
                }, StartupTimeout, cancellationToken).ConfigureAwait(false);
            try
            {
                _host = (testBuilder ?? throw new InvalidOperationException("Program did not configure a supported generic host."))
                    .Build(application);
                return _host;
            }
            catch (Exception exception)
            {
                try
                {
                    if (application is IAsyncDisposable asynchronousApplication)
                    {
                        await asynchronousApplication.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        application.Dispose();
                    }
                }
                catch (Exception cleanup)
                {
                    AzureFunctionTestHost.AddCleanupFailure(exception, cleanup);
                }

                throw;
            }
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed)
            {
                try
                {
                    if (_host is not null)
                    {
                        await _host.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    // Active scenarios reject disposal before it starts; service cleanup failures do not.
                    _disposed = _host is null || _host.IsDisposed;
                }
            }
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    private static async Task InvokeEntryPointAsync(MethodInfo entryPoint, string[] arguments)
    {
        try
        {
            var result = entryPoint.Invoke(null, entryPoint.GetParameters().Length == 0 ? null : [arguments]);
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void RegisterFunctions(IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters))
        {
            if (type.GetMethods().Any(method => method.IsDefined(typeof(FunctionAttribute), inherit: true)))
            {
                services.TryAddTransient(type);
            }
        }
    }
}
