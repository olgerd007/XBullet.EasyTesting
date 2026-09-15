using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Testcontainers;

/// <summary>
/// Owns a Testcontainers container for one scenario and publishes values after it is ready.
/// </summary>
public sealed class TestcontainerResource<TContainer> : ITestScenarioEnvironmentResource
    where TContainer : class, IContainer
{
    private readonly Func<TContainer, IReadOnlyDictionary<string, string?>> _configurationValues;
    private readonly Action<TContainer, IServiceCollection>? _configureServices;
    private readonly int _maximumDiagnosticCharacters;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates a scenario-owned container resource.</summary>
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
    public TContainer Container { get; }

    /// <summary>Starts the container and waits for its configured readiness strategy.</summary>
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
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureStarted();
        services.AddSingleton(Container);
        _configureServices?.Invoke(Container, services);
    }

    /// <summary>Captures container identity, state, ports, and bounded standard output and error.</summary>
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
