namespace XBullet.EasyTesting.Hosting;

/// <summary>Mutable test state that participates in scenario reset and failure diagnostics.</summary>
public interface ITestScenarioResource
{
    /// <summary>Clears mutable state before and after a scenario.</summary>
    /// <param name="cancellationToken">
    /// Cancels this reset attempt. The default token does not request cancellation. The factory
    /// continues resetting the remaining registered resources and reports all reset failures
    /// together.
    /// </param>
    /// <returns>A task that completes when the resource has finished resetting its state.</returns>
    ValueTask ResetAsync(CancellationToken cancellationToken = default);

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
