using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Aspire;

/// <summary>Owns a running closed-box Aspire distributed application.</summary>
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
    public DistributedApplication Application { get; }

    /// <summary>Gets diagnostics captured by <see cref="AspireTestHostBuilder{TAppHost}.RunAsync"/>.</summary>
    public AspireApplicationDiagnostics? Diagnostics { get; internal set; }

    /// <summary>Gets the names of resources in the AppHost model.</summary>
    public IReadOnlyList<string> ResourceNames => Application.Services
        .GetRequiredService<DistributedApplicationModel>()
        .Resources
        .Select(resource => resource.Name)
        .ToArray();

    /// <summary>Creates an HTTP client for a resource's preferred HTTP endpoint.</summary>
    public HttpClient CreateHttpClient(string resourceName)
    {
        EnsureNotDisposed();
        return Application.CreateHttpClient(resourceName);
    }

    /// <summary>Creates an HTTP client for a named resource endpoint.</summary>
    public HttpClient CreateHttpClient(string resourceName, string endpointName)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        return Application.CreateHttpClient(resourceName, endpointName);
    }

    /// <summary>Gets a resource's preferred endpoint.</summary>
    public Uri GetEndpoint(string resourceName)
    {
        EnsureNotDisposed();
        return Application.GetEndpoint(resourceName);
    }

    /// <summary>Gets a named resource endpoint.</summary>
    public Uri GetEndpoint(string resourceName, string endpointName)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        return Application.GetEndpoint(resourceName, endpointName);
    }

    /// <summary>Gets a resource connection string without exposing it in diagnostics.</summary>
    public ValueTask<string?> GetConnectionStringAsync(
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return Application.GetConnectionStringAsync(resourceName, cancellationToken);
    }

    /// <summary>Waits until a named resource is healthy or becomes unavailable.</summary>
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
            resources.Add(new AspireResourceDiagnostics(
                resource.Name,
                resourceEvent?.ResourceId,
                resourceEvent?.Snapshot.ResourceType,
                resourceEvent?.Snapshot.State?.Text,
                resourceEvent?.Snapshot.HealthStatus?.ToString(),
                resourceEvent?.Snapshot.ExitCode,
                logs.ToArray()));
        }

        return new AspireApplicationDiagnostics(DateTimeOffset.UtcNow, resources);
    }

    /// <inheritdoc />
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
            failures ??= [];
            failures.Add(exception);
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Aspire test application resources failed to clean up.",
                failures);
        }
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
