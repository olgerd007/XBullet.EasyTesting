namespace XBullet.EasyTesting.Hosting;

/// <summary>Diagnostics captured from a failed test scenario before its state is cleaned up.</summary>
/// <param name="ScenarioId">The stable identifier of the failed scenario.</param>
/// <param name="CapturedAt">The UTC timestamp at which diagnostic capture completed.</param>
/// <param name="Resources">
/// Diagnostic values keyed by their case-insensitive registered resource names. The diagnostics
/// object owns the dictionary but not necessarily objects supplied by individual resources.
/// </param>
public sealed record TestScenarioDiagnostics(
    string ScenarioId,
    DateTimeOffset CapturedAt,
    IReadOnlyDictionary<string, object?> Resources)
{
    /// <summary>The exception data key containing captured scenario diagnostics.</summary>
    public const string ExceptionDataKey = "XBullet.EasyTesting.TestScenarioDiagnostics";
}
