namespace XBullet.EasyTesting.Aspire;

/// <summary>State and recent console output for one Aspire resource.</summary>
public sealed class AspireResourceDiagnostics
{
    /// <summary>Creates immutable resource diagnostics.</summary>
    /// <param name="name">The non-empty AppHost resource name.</param>
    /// <param name="resourceId">The runtime resource identifier, or <see langword="null"/> when none is available.</param>
    /// <param name="resourceType">The Aspire resource type, or <see langword="null"/> when none is reported.</param>
    /// <param name="state">The Aspire runtime state, or <see langword="null"/> when no state is reported.</param>
    /// <param name="healthStatus">The Aspire health status, or <see langword="null"/> when no health status is reported.</param>
    /// <param name="exitCode">The process exit code, or <see langword="null"/> when the resource has not exited or no code is available.</param>
    /// <param name="logs">
    /// The captured log lines in their reported order. The list is retained without being copied;
    /// callers should not mutate it after construction.
    /// </param>
    public AspireResourceDiagnostics(
        string name,
        string? resourceId,
        string? resourceType,
        string? state,
        string? healthStatus,
        int? exitCode,
        IReadOnlyList<AspireResourceLogEntry> logs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(logs);
        Name = name;
        ResourceId = resourceId;
        ResourceType = resourceType;
        State = state;
        HealthStatus = healthStatus;
        ExitCode = exitCode;
        Logs = logs;
    }

    /// <summary>Gets the AppHost resource name.</summary>
    /// <value>The non-empty name assigned to the resource in the AppHost model.</value>
    public string Name { get; }

    /// <summary>Gets the current resource instance identifier, when available.</summary>
    /// <value>The runtime resource identifier, or <see langword="null"/> when none is available.</value>
    public string? ResourceId { get; }

    /// <summary>Gets the Aspire resource type, when available.</summary>
    /// <value>The Aspire resource type, or <see langword="null"/> when none is reported.</value>
    public string? ResourceType { get; }

    /// <summary>Gets the current resource state, when available.</summary>
    /// <value>The Aspire runtime state, or <see langword="null"/> when no state is reported.</value>
    public string? State { get; }

    /// <summary>Gets the current health status, when available.</summary>
    /// <value>The Aspire health status, or <see langword="null"/> when no health status is reported.</value>
    public string? HealthStatus { get; }

    /// <summary>Gets the process exit code, when available.</summary>
    /// <value>
    /// The process exit code when the resource has exited and Aspire reports one; otherwise,
    /// <see langword="null"/>.
    /// </value>
    public int? ExitCode { get; }

    /// <summary>Gets bounded recent stdout and stderr lines.</summary>
    /// <value>
    /// The recent standard-output and standard-error lines in their reported order, limited by the
    /// configured maximum. The returned list should be treated as read-only. Log content is not
    /// redacted and can contain sensitive data.
    /// </value>
    public IReadOnlyList<AspireResourceLogEntry> Logs { get; }
}
