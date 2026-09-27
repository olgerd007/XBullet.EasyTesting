using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Aspire;

/// <summary>Owns a running closed-box Aspire distributed application.</summary>
/// <typeparam name="TAppHost">The AppHost entry-point type used to create the distributed application.</typeparam>
/// <remarks>
/// Dispose this instance to stop the application and release its testing builder. The native
/// application exposed by <see cref="Application"/> is owned by this instance and must not be
/// disposed separately.
/// </remarks>
public sealed class AspireTestApplication<TAppHost> : IAsyncDisposable
    where TAppHost : class
{
    private readonly IDistributedApplicationTestingBuilder _testingBuilder;
    private readonly int _maximumDiagnosticLinesPerResource;
    private bool _disposed;

    internal AspireTestApplication(
        IDistributedApplicationTestingBuilder testingBuilder,
        DistributedApplication application,
        int maximumDiagnosticLinesPerResource)
    {
        _testingBuilder = testingBuilder;
        Application = application;
        _maximumDiagnosticLinesPerResource = maximumDiagnosticLinesPerResource;
    }

    /// <summary>Gets the native running Aspire distributed application.</summary>
    /// <value>
    /// The running application owned by this wrapper. Callers may use it but must not dispose it
    /// separately.
    /// </value>
    public DistributedApplication Application { get; }

    /// <summary>Gets diagnostics captured by <see cref="AspireTestHostBuilder{TAppHost}.RunAsync"/>.</summary>
    /// <value>
    /// The failure diagnostics captured by <c>RunAsync</c>, or <see langword="null"/> until a test
    /// failure has been captured. The snapshot can be retained after this application is disposed.
    /// </value>
    public AspireApplicationDiagnostics? Diagnostics { get; internal set; }

    /// <summary>Gets the names of resources in the AppHost model.</summary>
    /// <value>
    /// A newly allocated snapshot of resource names in AppHost model order. The caller owns the
    /// returned list and may retain it.
    /// </value>
    public IReadOnlyList<string> ResourceNames => Application.Services
        .GetRequiredService<DistributedApplicationModel>()
        .Resources
        .Select(resource => resource.Name)
        .ToArray();

    /// <summary>Creates an HTTP client for a resource's preferred HTTP endpoint.</summary>
    /// <param name="resourceName">
    /// The name of a configured resource with a preferred HTTP endpoint. Aspire validates the
    /// name and endpoint availability.
    /// </param>
    /// <returns>A new HTTP client that the caller owns and must dispose.</returns>
    public HttpClient CreateHttpClient(string resourceName)
    {
        EnsureNotDisposed();
        return Application.CreateHttpClient(resourceName);
    }

    /// <summary>Creates an HTTP client for a named resource endpoint.</summary>
    /// <param name="resourceName">
    /// The name of a configured resource. Aspire validates the name and endpoint availability.
    /// </param>
    /// <param name="endpointName">The non-empty name of the endpoint to use.</param>
    /// <returns>A new HTTP client that the caller owns and must dispose.</returns>
    public HttpClient CreateHttpClient(string resourceName, string endpointName)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        return Application.CreateHttpClient(resourceName, endpointName);
    }

    /// <summary>Gets a resource's preferred endpoint.</summary>
    /// <param name="resourceName">
    /// The name of a configured resource with a preferred endpoint. Aspire validates the name and
    /// endpoint availability.
    /// </param>
    /// <returns>The endpoint URI reported by Aspire. The immutable URI may be retained by the caller.</returns>
    public Uri GetEndpoint(string resourceName)
    {
        EnsureNotDisposed();
        return Application.GetEndpoint(resourceName);
    }

    /// <summary>Gets a named resource endpoint.</summary>
    /// <param name="resourceName">
    /// The name of a configured resource. Aspire validates the name and endpoint availability.
    /// </param>
    /// <param name="endpointName">The non-empty name of the endpoint to retrieve.</param>
    /// <returns>The endpoint URI reported by Aspire. The immutable URI may be retained by the caller.</returns>
    public Uri GetEndpoint(string resourceName, string endpointName)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        return Application.GetEndpoint(resourceName, endpointName);
    }

    /// <summary>Gets a resource connection string without exposing it in diagnostics.</summary>
    /// <param name="resourceName">
    /// The name of the configured resource whose connection string should be resolved. Aspire
    /// validates the resource name.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that cancels connection-string resolution. The default token does not request
    /// cancellation.
    /// </param>
    /// <returns>
    /// A value task whose result is the configured connection string, or <see langword="null"/>
    /// when no connection string is available. The returned value can contain secrets; callers
    /// must protect it and must not assume that diagnostics redact copies they write to logs.
    /// </returns>
    public ValueTask<string?> GetConnectionStringAsync(
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return Application.GetConnectionStringAsync(resourceName, cancellationToken);
    }

    /// <summary>Waits until a named resource is healthy or becomes unavailable.</summary>
    /// <param name="resourceName">The non-empty name of the configured resource to monitor.</param>
    /// <param name="cancellationToken">
    /// A token that cancels the wait. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A task that completes when the resource becomes healthy and faults if it becomes unavailable.
    /// </returns>
    public Task WaitForResourceAsync(
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return Application.ResourceNotifications.WaitForResourceHealthyAsync(
            resourceName,
            WaitBehavior.StopOnResourceUnavailable,
            cancellationToken);
    }

    /// <summary>Captures current resource states and bounded recent logs.</summary>
    /// <param name="cancellationToken">
    /// A token that cancels log enumeration. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// A value task whose result is a new diagnostics snapshot that the caller may retain. It
    /// includes at most the configured number of recent log lines per resource; zero disables log
    /// capture. Connection strings and endpoint values are excluded, but log content is not redacted.
    /// </returns>
    public async ValueTask<AspireApplicationDiagnostics> CaptureDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        var model = Application.Services.GetRequiredService<DistributedApplicationModel>();
        var logService = Application.Services.GetRequiredService<ResourceLoggerService>();
        var resources = new List<AspireResourceDiagnostics>();
        foreach (var resource in model.Resources)
        {
            var logs = new Queue<AspireResourceLogEntry>();
            await foreach (var batch in logService
                .GetAllAsync(resource)
                .WithCancellation(cancellationToken))
            {
                foreach (var line in batch)
                {
                    if (_maximumDiagnosticLinesPerResource == 0)
                    {
                        continue;
                    }

                    logs.Enqueue(new AspireResourceLogEntry(
                        line.LineNumber,
                        line.Content,
                        line.IsErrorMessage));
                    if (logs.Count > _maximumDiagnosticLinesPerResource)
                    {
                        logs.Dequeue();
                    }
                }
            }

            Application.ResourceNotifications.TryGetCurrentState(
                resource.Name,
                out var resourceEvent);
            if (resourceEvent is null)
            {
                resources.Add(new AspireResourceDiagnostics(
                    resource.Name,
                    resourceId: null,
                    resourceType: null,
                    state: null,
                    healthStatus: null,
                    exitCode: null,
                    logs.ToArray()));
                continue;
            }

            var snapshot = resourceEvent.Snapshot;
            resources.Add(new AspireResourceDiagnostics(
                resource.Name,
                resourceEvent.ResourceId,
                snapshot.ResourceType,
                snapshot.State?.Text,
                snapshot.HealthStatus?.ToString(),
                snapshot.ExitCode,
                logs.ToArray()));
        }

        return new AspireApplicationDiagnostics(DateTimeOffset.UtcNow, resources);
    }

    /// <summary>Stops the application and releases the native application and testing builder.</summary>
    /// <returns>
    /// A value task that completes after both owned resources have been given a chance to dispose.
    /// Repeated calls have no effect; multiple cleanup failures are reported as an aggregate exception.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        try
        {
            await Application.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures = [exception];
        }

        try
        {
            await _testingBuilder.DisposeAsync();
        }
        catch (Exception exception)
        {
            failures = AddCleanupFailure(failures, exception);
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Aspire test application resources failed to clean up.",
                failures);
        }
    }

    internal static List<Exception> AddCleanupFailure(
        List<Exception>? failures,
        Exception exception)
    {
        failures ??= [];
        failures.Add(exception);
        return failures;
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
