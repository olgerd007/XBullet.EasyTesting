namespace XBullet.EasyTesting.Hosting;

/// <summary>Diagnostics captured from a failed test scenario before its state is cleaned up.</summary>
public sealed record TestScenarioDiagnostics(
    string ScenarioId,
    DateTimeOffset CapturedAt,
    IReadOnlyDictionary<string, object?> Resources)
{
    /// <summary>The exception data key containing captured scenario diagnostics.</summary>
    public const string ExceptionDataKey = "XBullet.EasyTesting.TestScenarioDiagnostics";
}
