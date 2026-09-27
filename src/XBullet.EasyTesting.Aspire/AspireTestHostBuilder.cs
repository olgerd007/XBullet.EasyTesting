using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace XBullet.EasyTesting.Aspire;

/// <summary>Configures and starts an isolated Aspire distributed application.</summary>
/// <typeparam name="TAppHost">The AppHost entry-point type used to create the distributed application.</typeparam>
/// <remarks>
/// This mutable builder is not thread-safe and can start only one application. A start attempt
/// consumes the builder even when startup fails.
/// </remarks>
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
    /// <param name="arguments">
    /// The arguments to append in order. The array and its elements must be non-null; empty strings
    /// and an empty array are accepted.
    /// </param>
    /// <returns>This builder, for chaining. The supplied array is not retained.</returns>
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
    /// <param name="configure">
    /// A non-null callback invoked once, in registration order, after the native testing builder is
    /// created and before the application is built. The callback may configure the builder but must
    /// not retain or dispose it because the started test application owns it.
    /// </param>
    /// <returns>This builder, for chaining. The callback is retained until startup.</returns>
    public AspireTestHostBuilder<TAppHost> ConfigureAppHost(
        Action<IDistributedApplicationTestingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotStarted();
        _configureActions.Add(configure);
        return this;
    }

    /// <summary>Adds a resource that must be healthy before the test application is returned.</summary>
    /// <param name="resourceName">
    /// The non-empty AppHost resource name. Duplicate names are ignored using an ordinal,
    /// case-insensitive comparison.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
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
    /// <param name="timeout">
    /// A positive duration covering AppHost creation, startup, and all configured resource waits.
    /// The default is two minutes.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public AspireTestHostBuilder<TAppHost> WithStartupTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        EnsureNotStarted();
        _startupTimeout = timeout;
        return this;
    }

    /// <summary>Sets the maximum number of recent log lines retained per diagnostic resource.</summary>
    /// <param name="maximumLines">
    /// The non-negative number of lines retained for each resource. The default is 500 lines;
    /// zero disables log capture, and older lines are discarded first.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public AspireTestHostBuilder<TAppHost> WithMaximumDiagnosticLinesPerResource(int maximumLines)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLines);
        EnsureNotStarted();
        _maximumDiagnosticLinesPerResource = maximumLines;
        return this;
    }

    /// <summary>Starts the AppHost and waits for all configured resources to become healthy.</summary>
    /// <param name="cancellationToken">
    /// A token that cancels AppHost creation, startup, and resource waits. The default token does
    /// not request cancellation. The configured startup timeout independently cancels those operations.
    /// </param>
    /// <returns>
    /// A task whose result owns the running application and native testing builder. The caller must
    /// asynchronously dispose the result. On failure, partially created resources are disposed.
    /// </returns>
    /// <remarks>
    /// Caller-requested cancellation is reported as cancellation. Expiry of the configured startup
    /// timeout is reported as a <see cref="TimeoutException"/>.
    /// </remarks>
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
    /// <param name="test">
    /// A non-null asynchronous callback invoked once with the running application and the caller's
    /// cancellation token. The application is owned by this method; the callback must not dispose
    /// or retain it beyond completion.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used during startup and passed unchanged to <paramref name="test"/>. The default token
    /// does not request cancellation. Failure diagnostics and cleanup are attempted without this token.
    /// </param>
    /// <returns>
    /// A task that completes after the callback and application cleanup. If the callback fails,
    /// current diagnostics are attached to the exception before it is rethrown.
    /// </returns>
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
