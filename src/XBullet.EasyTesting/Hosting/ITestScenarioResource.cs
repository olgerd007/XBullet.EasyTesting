namespace XBullet.EasyTesting.Hosting;

/// <summary>Mutable test state that participates in scenario reset and failure diagnostics.</summary>
public interface ITestScenarioResource
{
    /// <summary>Clears mutable state before and after a scenario.</summary>
    ValueTask ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>Captures the current state before a failed scenario is cleaned up.</summary>
    ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default);
}
