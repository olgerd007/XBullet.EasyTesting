using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

// Hosting emits these events for both HostBuilder and HostApplicationBuilder. Stopping at HostBuilt
// leaves the production container intact without starting hosted services or the worker transport.
internal sealed class FunctionProgramHostCapture(Action<IHostBuilder> configure)
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private static readonly AsyncLocal<FunctionProgramHostCapture?> Current = new();
    private readonly List<IDisposable> _subscriptions = [];
    private IHost? _host;
    private bool _building;

    internal static async Task<IHost> CaptureAsync(
        Func<Task> entryPoint,
        Action<IHostBuilder> configure,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(configure);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var capture = new FunctionProgramHostCapture(configure);
        // Entry points can synchronously block before returning a task. Keep that application code
        // off the caller's thread; cancellation bounds the wait and late hosts are still intercepted.
        var startup = Task.Run(() => capture.RunAsync(entryPoint), CancellationToken.None);
        try
        {
            return await startup.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Do not unsubscribe on timeout: late Build calls must still be stopped before Run.
            _ = DisposeLateHostAsync(startup, exception);
            throw;
        }
    }

    private async Task<IHost> RunAsync(Func<Task> entryPoint)
    {
        Current.Value = this;
        using var allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        try
        {
            try
            {
                await entryPoint().ConfigureAwait(false);
            }
            catch (HostAbortedException) when (_host is not null)
            {
                // This is the intentional stop from HostBuilt, before Program can call Run.
            }

            return _host ?? throw new InvalidOperationException("The Functions entry point exited without building an IHost.");
        }
        catch (Exception exception)
        {
            if (_host is not null)
            {
                await DisposeHostSafelyAsync(_host, exception).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            Current.Value = null;
        }
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (ReferenceEquals(Current.Value, this) && listener.Name == "Microsoft.Extensions.Hosting")
        {
            _subscriptions.Add(listener.Subscribe(this));
        }
    }

    public void OnNext(KeyValuePair<string, object?> notification)
    {
        if (!ReferenceEquals(Current.Value, this))
        {
            return;
        }

        if (notification.Key == "HostBuilding" && notification.Value is IHostBuilder builder && !_building)
        {
            _building = true;
            configure(builder);
        }
        else if (notification.Key == "HostBuilt" && notification.Value is IHost host)
        {
            _host = host;
            throw new HostAbortedException();
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    private static async Task DisposeLateHostAsync(Task<IHost> startup, Exception failure)
    {
        IHost host;
        try
        {
            host = await startup.ConfigureAwait(false);
        }
        catch (Exception startupFailure)
        {
            // A failure after the canceled wait cannot be rethrown to that caller. Retain it
            // on the original exception instead; RunAsync disposes any host it captured.
            if (!ReferenceEquals(failure, startupFailure))
            {
                failure.Data["XBullet.EasyTesting.FunctionStartupException"] = startupFailure;
            }

            return;
        }

        await DisposeHostSafelyAsync(host, failure).ConfigureAwait(false);
    }

    private static async Task DisposeHostSafelyAsync(IHost host, Exception failure)
    {
        try
        {
            if (host is IAsyncDisposable asynchronousHost)
            {
                await asynchronousHost.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception cleanup)
        {
            AzureFunctionTestHost.AddCleanupFailure(failure, cleanup);
        }
    }
}
