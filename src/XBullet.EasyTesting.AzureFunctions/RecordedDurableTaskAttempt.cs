namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Identifies the kind of connected task invocation.</summary>
public enum DurableTaskAttemptKind
{
    /// <summary>An activity invocation.</summary>
    Activity,
    /// <summary>A child orchestration invocation.</summary>
    SubOrchestration
}

/// <summary>Identifies the outcome of an actual connected task attempt.</summary>
public enum DurableTaskAttemptStatus
{
    /// <summary>The attempt is executing.</summary>
    Running,
    /// <summary>The attempt returned successfully.</summary>
    Succeeded,
    /// <summary>The attempt failed.</summary>
    Failed,
    /// <summary>The attempt threw cancellation.</summary>
    Canceled
}

/// <summary>Describes an immutable snapshot of one connected task attempt.</summary>
/// <param name="Sequence">The one-based workflow-wide attempt sequence.</param>
/// <param name="CallId">The one-based logical call ID, shared by its attempts.</param>
/// <param name="InstanceId">The calling orchestrator's instance ID.</param>
/// <param name="TaskName">The registered task name.</param>
/// <param name="Kind">The task category.</param>
/// <param name="AttemptNumber">The one-based attempt number within the call.</param>
/// <param name="ChildInstanceId">The actual child ID, or null for an activity.</param>
/// <param name="StartedAt">The calling context's UTC time at invocation.</param>
/// <param name="CompletedAt">The calling context's UTC time at completion, or null while running.</param>
/// <param name="Status">The recorded outcome.</param>
/// <param name="RetryAt">The planned next attempt's UTC time, or null when no retry has been accepted.</param>
/// <param name="Failure">The original failure, retained for explicit inspection; excluded from diagnostics.</param>
public sealed record RecordedDurableTaskAttempt(long Sequence, long CallId, string InstanceId, string TaskName,
    DurableTaskAttemptKind Kind, int AttemptNumber, string? ChildInstanceId, DateTime StartedAt,
    DateTime? CompletedAt, DurableTaskAttemptStatus Status, DateTime? RetryAt, Exception? Failure);
