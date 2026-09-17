namespace XBullet.EasyTesting.Aspire;

/// <summary>State and recent console output for one Aspire resource.</summary>
public sealed class AspireResourceDiagnostics
{
    /// <summary>Creates immutable resource diagnostics.</summary>
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
    public string Name { get; }

    /// <summary>Gets the current resource instance identifier, when available.</summary>
    public string? ResourceId { get; }

    /// <summary>Gets the Aspire resource type, when available.</summary>
    public string? ResourceType { get; }

    /// <summary>Gets the current resource state, when available.</summary>
    public string? State { get; }

    /// <summary>Gets the current health status, when available.</summary>
    public string? HealthStatus { get; }

    /// <summary>Gets the process exit code, when available.</summary>
    public int? ExitCode { get; }

    /// <summary>Gets bounded recent stdout and stderr lines.</summary>
    public IReadOnlyList<AspireResourceLogEntry> Logs { get; }
}
