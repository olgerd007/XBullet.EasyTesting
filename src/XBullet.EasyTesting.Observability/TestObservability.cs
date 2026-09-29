using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Observability;

/// <summary>Owns deterministic time and captured observability signals for one scenario.</summary>
public sealed class TestObservability : ITestScenarioEnvironmentResource
{
    /// <summary>Creates scenario observability from the supplied options.</summary>
    /// <param name="options">
    /// The configuration to read immediately, or <see langword="null"/> to use defaults. Maximums
    /// must be positive. The options object is not retained, and later changes do not affect this resource.
    /// </param>
    /// <remarks>
    /// This resource owns its three collectors and any fake time provider. Callers may use the
    /// exposed objects while the scenario is active but must not dispose them separately.
    /// </remarks>
    public TestObservability(TestObservabilityOptions? options = null)
    {
        options ??= new TestObservabilityOptions();
        if (options.MaximumLogEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaximumLogEntries must be greater than zero.");
        }

        if (options.MaximumActivityEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaximumActivityEntries must be greater than zero.");
        }

        if (options.MaximumMetricMeasurements <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaximumMetricMeasurements must be greater than zero.");
        }

        if (options.UsesFakeTime)
        {
            Time = options.FakeTimeStart is null
                ? new FakeTimeProvider()
                : new FakeTimeProvider(options.FakeTimeStart.Value);
        }

        Logs = new TestLogCollector(Time, options.MaximumLogEntries);
        Activities = new TestActivityCollector(
            options.ActivitySourceNames,
            options.MaximumActivityEntries);
        Metrics = new TestMetricCollector(
            Time,
            options.MeterNames,
            options.MaximumMetricMeasurements);
    }

    /// <summary>Gets the structured application-log collector.</summary>
    /// <value>The scenario-owned bounded collector registered as the host's logging provider.</value>
    public TestLogCollector Logs { get; }

    /// <summary>Gets the completed-activity collector.</summary>
    /// <value>The scenario-owned bounded listener for completed activities.</value>
    public TestActivityCollector Activities { get; }

    /// <summary>Gets the metric-measurement collector.</summary>
    /// <value>The scenario-owned bounded listener for supported numeric metric measurements.</value>
    public TestMetricCollector Metrics { get; }

    /// <summary>Gets deterministic time when enabled, otherwise null.</summary>
    /// <value>
    /// The scenario-owned fake time provider registered as the application's <see cref="TimeProvider"/>,
    /// or <see langword="null"/> when system time remains configured. It also timestamps captured logs
    /// and metrics, but activity timestamps continue to come from <c>Activity</c>.
    /// </value>
    public FakeTimeProvider? Time { get; }

    /// <inheritdoc />
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void ConfigureConfiguration(IConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(this);
        services.AddSingleton(Logs);
        services.AddSingleton<ILoggerProvider>(Logs);
        if (Time is not null)
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        }
    }

    /// <inheritdoc />
    public async ValueTask<object?> CaptureDiagnosticsAsync(
        CancellationToken cancellationToken = default) =>
        new
        {
            FakeUtcNow = Time?.GetUtcNow(),
            Logs = await Logs.CaptureDiagnosticsAsync(cancellationToken),
            Activities = await Activities.CaptureDiagnosticsAsync(cancellationToken),
            Metrics = await Metrics.CaptureDiagnosticsAsync(cancellationToken)
        };

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Metrics.Dispose();
        Activities.Dispose();
        Logs.Dispose();
        return ValueTask.CompletedTask;
    }
}
