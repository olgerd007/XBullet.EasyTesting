namespace XBullet.EasyTesting.Aspire;

/// <summary>Diagnostics captured from a running Aspire distributed application.</summary>
public sealed class AspireApplicationDiagnostics
{
    /// <summary>The exception-data key used by distributed test failures.</summary>
    public const string ExceptionDataKey = "XBullet.EasyTesting.Aspire.Diagnostics";

    /// <summary>Creates an immutable diagnostics snapshot.</summary>
    /// <param name="capturedAt">The absolute instant at which the diagnostics were captured.</param>
    /// <param name="resources">
    /// The resource diagnostics in AppHost model order. The list is retained without being copied;
    /// callers should not mutate it after construction.
    /// </param>
    public AspireApplicationDiagnostics(
        DateTimeOffset capturedAt,
        IReadOnlyList<AspireResourceDiagnostics> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        CapturedAt = capturedAt;
        Resources = resources;
    }

    /// <summary>Gets the capture timestamp.</summary>
    /// <value>The absolute instant at which the snapshot was created.</value>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>Gets resource state and log diagnostics.</summary>
    /// <value>
    /// The resource diagnostics in AppHost model order. The returned list is the instance supplied
    /// to the constructor and should be treated as read-only.
    /// </value>
    public IReadOnlyList<AspireResourceDiagnostics> Resources { get; }
}
