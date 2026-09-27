using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Testcontainers;

/// <summary>
/// Owns a Testcontainers container for one scenario and publishes values after it is ready.
/// </summary>
/// <typeparam name="TContainer">The concrete Testcontainers container type owned by this resource.</typeparam>
public sealed class TestcontainerResource<TContainer> : ITestScenarioEnvironmentResource
    where TContainer : class, IContainer
{
    private readonly Func<TContainer, IReadOnlyDictionary<string, string?>> _configurationValues;
    private readonly Action<TContainer, IServiceCollection>? _configureServices;
    private readonly int _maximumDiagnosticCharacters;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates a scenario-owned container resource.</summary>
    /// <param name="container">
    /// The non-null, unstarted container whose ownership transfers to this resource immediately.
    /// It is disposed even if it is never started.
    /// </param>
    /// <param name="configurationValues">
    /// An optional callback invoked by <see cref="ConfigureConfiguration"/> after readiness. It must
    /// return a non-null dictionary with non-empty keys; null values are accepted. The callback must
    /// not dispose the container. The default publishes no values.
    /// </param>
    /// <param name="configureServices">
    /// An optional callback invoked by <see cref="ConfigureServices"/> after the container is registered
    /// as a singleton. It may update services in place but must not retain the collection or dispose the container.
    /// </param>
    /// <param name="maximumDiagnosticCharacters">
    /// The non-negative maximum trailing character count retained independently for standard output
    /// and standard error. The default is 20,000 characters; zero retains empty log strings.
    /// </param>
    public TestcontainerResource(
        TContainer container,
        Func<TContainer, IReadOnlyDictionary<string, string?>>? configurationValues = null,
        Action<TContainer, IServiceCollection>? configureServices = null,
        int maximumDiagnosticCharacters = 20_000)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDiagnosticCharacters);
        Container = container;
        _configurationValues = configurationValues ?? (_ =>
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        _configureServices = configureServices;
        _maximumDiagnosticCharacters = maximumDiagnosticCharacters;
    }

    /// <summary>Gets the native Testcontainers container.</summary>
    /// <value>
    /// The container owned by this resource. Callers may use it until resource disposal but must not
    /// dispose it separately.
    /// </value>
    public TContainer Container { get; }

    /// <summary>Starts the container and waits for its configured readiness strategy.</summary>
    /// <param name="cancellationToken">
    /// A token passed to Testcontainers startup and readiness operations. The default token does not
    /// request cancellation.
    /// </param>
    /// <returns>
    /// A value task that completes when the container is ready. A successful start may occur only
    /// once; a failed or canceled start leaves the resource eligible for another attempt.
    /// </returns>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The test container resource has already been started.");
        }

        await Container.StartAsync(cancellationToken);
        _started = true;
    }

    /// <summary>Adds values resolved from the ready container to application configuration.</summary>
    /// <param name="configuration">
    /// The non-null mutable configuration builder to update in place. The published values are
    /// resolved on this call and may contain secrets; they are not included in resource diagnostics.
    /// </param>
    public void ConfigureConfiguration(IConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureStarted();
        var values = _configurationValues(Container)
            ?? throw new InvalidOperationException("The test container configuration factory returned null.");

        foreach (var key in values.Keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
        }

        configuration.AddInMemoryCollection(values);
    }

    /// <summary>Registers the native container and applies optional scenario service overrides.</summary>
    /// <param name="services">
    /// The non-null mutable scenario service collection. The container is added as a singleton before
    /// the optional service callback runs; the scenario retains container disposal ownership.
    /// </param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureStarted();
        services.AddSingleton(Container);
        _configureServices?.Invoke(Container, services);
    }

    /// <summary>Captures container identity, state, ports, and bounded standard output and error.</summary>
    /// <param name="cancellationToken">
    /// A token passed to log retrieval. The default token does not request cancellation. Cancellation
    /// is propagated; other log failures are represented in the returned diagnostics.
    /// </param>
    /// <returns>
    /// A value task whose result reports only <c>Started = false</c> before startup. After startup it
    /// includes identity, image, hostname, state, health, mapped ports, the trailing configured number
    /// of log characters, and any log-capture error. Logs and error text are not redacted and may
    /// contain sensitive data; published configuration values are excluded.
    /// </returns>
    public async ValueTask<object?> CaptureDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return new { Started = false };
        }

        string? standardOutput = null;
        string? standardError = null;
        string? logCaptureError = null;
        try
        {
            var logs = await Container.GetLogsAsync(
                DateTime.MinValue,
                DateTime.MaxValue,
                timestampsEnabled: true,
                cancellationToken);
            standardOutput = Truncate(logs.Stdout);
            standardError = Truncate(logs.Stderr);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logCaptureError = exception.Message;
        }

        return new
        {
            Started = true,
            Container.Id,
            Container.Name,
            Image = Container.Image.FullName,
            Container.Hostname,
            State = Container.State.ToString(),
            Health = Container.Health.ToString(),
            Ports = Container.GetMappedPublicPorts(),
            StandardOutput = standardOutput,
            StandardError = standardError,
            LogCaptureError = logCaptureError
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Container.DisposeAsync();
    }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            throw new InvalidOperationException(
                "The test container must be started before it configures the scenario host.");
        }
    }

    private string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= _maximumDiagnosticCharacters)
        {
            return value;
        }

        return value[^_maximumDiagnosticCharacters..];
    }
}
