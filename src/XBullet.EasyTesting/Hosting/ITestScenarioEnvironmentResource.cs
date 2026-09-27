using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.Hosting;

/// <summary>
/// An external dependency that starts before a scenario host and contributes dynamic test
/// configuration or services.
/// </summary>
public interface ITestScenarioEnvironmentResource : IAsyncDisposable
{
    /// <summary>Starts the external dependency and waits until it is ready for the scenario.</summary>
    /// <param name="cancellationToken">
    /// Cancels startup and readiness checks. When cancellation prevents scenario creation, the
    /// scenario context still disposes the resource. The default token does not request
    /// cancellation.
    /// </param>
    /// <returns>A task that completes when the dependency is ready for the test host.</returns>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds configuration values after the external dependency has started.</summary>
    /// <param name="configuration">
    /// The application configuration builder to mutate. The caller owns the builder; the resource
    /// must not retain or dispose it.
    /// </param>
    void ConfigureConfiguration(IConfigurationBuilder configuration);

    /// <summary>Adds or replaces services after the external dependency has started.</summary>
    /// <param name="services">
    /// The scenario's service collection to mutate before the test host is built. The caller owns
    /// the collection; the resource must not retain or dispose it.
    /// </param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Captures the current state before a failed scenario is cleaned up.</summary>
    /// <param name="cancellationToken">
    /// Cancels diagnostic collection for this resource. The default token does not request
    /// cancellation. A thrown exception, including cancellation, is recorded as a diagnostic
    /// capture failure so cleanup can continue.
    /// </param>
    /// <returns>
    /// A serializable diagnostic value, or <see langword="null"/> when the resource has no state to
    /// report. Ownership remains with the resource unless its implementation states otherwise.
    /// </returns>
    ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default);
}
