namespace XBullet.EasyTesting.Aspire;

/// <summary>Diagnostics captured from a running Aspire distributed application.</summary>
public sealed class AspireApplicationDiagnostics
{
    /// <summary>The exception-data key used by distributed test failures.</summary>
    public const string ExceptionDataKey = "XBullet.EasyTesting.Aspire.Diagnostics";

    /// <summary>Creates an immutable diagnostics snapshot.</summary>
    public AspireApplicationDiagnostics(
        DateTimeOffset capturedAt,
        IReadOnlyList<AspireResourceDiagnostics> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        CapturedAt = capturedAt;
        Resources = resources;
    }

    /// <summary>Gets the capture timestamp.</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>Gets resource state and log diagnostics.</summary>
    public IReadOnlyList<AspireResourceDiagnostics> Resources { get; }
}
