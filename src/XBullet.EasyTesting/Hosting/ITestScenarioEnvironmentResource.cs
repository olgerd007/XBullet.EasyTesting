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
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds configuration values after the external dependency has started.</summary>
    void ConfigureConfiguration(IConfigurationBuilder configuration);

    /// <summary>Adds or replaces services after the external dependency has started.</summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Captures the current state before a failed scenario is cleaned up.</summary>
    ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default);
}
