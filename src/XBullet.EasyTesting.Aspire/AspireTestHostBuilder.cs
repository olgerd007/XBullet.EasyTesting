using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace XBullet.EasyTesting.Aspire;

/// <summary>Configures and starts an isolated Aspire distributed application.</summary>
public sealed class AspireTestHostBuilder<TAppHost>
    where TAppHost : class
{
    private readonly List<string> _arguments = [];
    private readonly List<Action<IDistributedApplicationTestingBuilder>> _configureActions = [];
    private readonly List<string> _resourcesToWaitFor = [];
    private TimeSpan _startupTimeout = TimeSpan.FromMinutes(2);
    private int _maximumDiagnosticLinesPerResource = 500;
    private bool _started;

    internal AspireTestHostBuilder()
    {
    }

    /// <summary>Adds command-line arguments passed to the AppHost entry point.</summary>
    public AspireTestHostBuilder<TAppHost> WithArguments(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        EnsureNotStarted();
        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            _arguments.Add(argument);
        }

        return this;
    }

    /// <summary>Configures the native distributed application testing builder.</summary>
    public AspireTestHostBuilder<TAppHost> ConfigureAppHost(
        Action<IDistributedApplicationTestingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotStarted();
        _configureActions.Add(configure);
        return this;
    }

    /// <summary>Adds a resource that must be healthy before the test application is returned.</summary>
    public AspireTestHostBuilder<TAppHost> WaitForResource(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        EnsureNotStarted();
        if (!_resourcesToWaitFor.Contains(resourceName, StringComparer.OrdinalIgnoreCase))
        {
            _resourcesToWaitFor.Add(resourceName);
        }

        return this;
    }

    /// <summary>Sets the maximum time allowed for AppHost and resource readiness.</summary>
    public AspireTestHostBuilder<TAppHost> WithStartupTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        EnsureNotStarted();
        _startupTimeout = timeout;
        return this;
    }

    /// <summary>Sets the maximum number of recent log lines retained per diagnostic resource.</summary>
    public AspireTestHostBuilder<TAppHost> WithMaximumDiagnosticLinesPerResource(int maximumLines)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLines);
        EnsureNotStarted();
        _maximumDiagnosticLinesPerResource = maximumLines;
        return this;
    }

    /// <summary>Starts the AppHost and waits for all configured resources to become healthy.</summary>
    public async Task<AspireTestApplication<TAppHost>> StartAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureNotStarted();
        _started = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_startupTimeout);
        IDistributedApplicationTestingBuilder? testingBuilder = null;
        DistributedApplication? application = null;
        try
        {
            testingBuilder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>(
                _arguments.ToArray(),
                timeout.Token);
            foreach (var configure in _configureActions)
            {
                configure(testingBuilder);
            }

            application = testingBuilder.Build();
            await application.StartAsync(timeout.Token);
            foreach (var resourceName in _resourcesToWaitFor)
            {
                await application.ResourceNotifications.WaitForResourceHealthyAsync(
                    resourceName,
                    WaitBehavior.StopOnResourceUnavailable,
                    timeout.Token);
            }

            return new AspireTestApplication<TAppHost>(
                testingBuilder,
                application,
                _maximumDiagnosticLinesPerResource);
        }
        catch (Exception exception)
        {
            var failure = exception is OperationCanceledException
                && !cancellationToken.IsCancellationRequested
                ? new TimeoutException(
                    $"The Aspire AppHost did not become ready within {_startupTimeout}.",
                    exception)
                : exception;
            try
            {
                if (application is not null)
                {
                    await application.DisposeAsync();
                }

                if (testingBuilder is not null)
                {
                    await testingBuilder.DisposeAsync();
                }
            }
            catch (Exception cleanupException)
            {
                failure.Data["XBullet.EasyTesting.Aspire.StartupCleanupException"] = cleanupException;
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    /// <summary>Runs a distributed test, attaching diagnostics to any failure.</summary>
    public async Task RunAsync(
        Func<AspireTestApplication<TAppHost>, CancellationToken, Task> test,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(test);
        await using var application = await StartAsync(cancellationToken);
        try
        {
            await test(application, cancellationToken);
        }
        catch (Exception exception)
        {
            try
            {
                var diagnostics = await application.CaptureDiagnosticsAsync(CancellationToken.None);
                application.Diagnostics = diagnostics;
                exception.Data[AspireApplicationDiagnostics.ExceptionDataKey] = diagnostics;
            }
            catch (Exception diagnosticsException)
            {
                exception.Data["XBullet.EasyTesting.Aspire.DiagnosticsException"] =
                    diagnosticsException;
            }

            throw;
        }
    }

    private void EnsureNotStarted()
    {
        if (_started)
        {
            throw new InvalidOperationException("The Aspire test host builder has already been started.");
        }
    }
}
