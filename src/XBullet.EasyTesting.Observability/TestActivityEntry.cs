using System.Diagnostics;

namespace XBullet.EasyTesting.Observability;

/// <summary>One completed activity captured from an integration-test process.</summary>
public sealed class TestActivityEntry
{
    /// <summary>Creates an immutable captured activity entry.</summary>
    /// <param name="sourceName">The activity source name.</param>
    /// <param name="operationName">The activity operation name.</param>
    /// <param name="displayName">The activity display name.</param>
    /// <param name="kind">The activity kind describing the span's role.</param>
    /// <param name="status">The completion status code.</param>
    /// <param name="statusDescription">The optional status description, or <see langword="null"/> when absent.</param>
    /// <param name="traceId">The W3C trace identifier.</param>
    /// <param name="spanId">The W3C span identifier.</param>
    /// <param name="parentSpanId">The parent span identifier, or its default value for a root activity.</param>
    /// <param name="startTime">The absolute UTC start instant reported by the activity.</param>
    /// <param name="duration">The elapsed activity duration.</param>
    /// <param name="tags">
    /// The activity tags. The dictionary is retained without copying and can contain sensitive or
    /// mutable values; callers should not mutate it after construction.
    /// </param>
    /// <param name="baggage">
    /// The activity baggage. The dictionary is retained without copying and can contain sensitive
    /// values; callers should not mutate it after construction.
    /// </param>
    /// <param name="events">
    /// The activity events in recorded order. The list is retained without copying and should not be
    /// mutated after construction.
    /// </param>
    public TestActivityEntry(
        string sourceName,
        string operationName,
        string displayName,
        ActivityKind kind,
        ActivityStatusCode status,
        string? statusDescription,
        ActivityTraceId traceId,
        ActivitySpanId spanId,
        ActivitySpanId parentSpanId,
        DateTimeOffset startTime,
        TimeSpan duration,
        IReadOnlyDictionary<string, object?> tags,
        IReadOnlyDictionary<string, string?> baggage,
        IReadOnlyList<TestActivityEvent> events)
    {
        SourceName = sourceName;
        OperationName = operationName;
        DisplayName = displayName;
        Kind = kind;
        Status = status;
        StatusDescription = statusDescription;
        TraceId = traceId;
        SpanId = spanId;
        ParentSpanId = parentSpanId;
        StartTime = startTime;
        Duration = duration;
        Tags = tags;
        Baggage = baggage;
        Events = events;
    }

    /// <summary>Gets the activity source name.</summary>
    /// <value>The source name captured at completion.</value>
    public string SourceName { get; }

    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name captured at completion.</value>
    public string OperationName { get; }

    /// <summary>Gets the display name.</summary>
    /// <value>The display name captured at completion.</value>
    public string DisplayName { get; }

    /// <summary>Gets the activity kind.</summary>
    /// <value>The span role reported by <see cref="ActivityKind"/>.</value>
    public ActivityKind Kind { get; }

    /// <summary>Gets the activity status.</summary>
    /// <value>The completion status reported by the activity.</value>
    public ActivityStatusCode Status { get; }

    /// <summary>Gets the activity status description.</summary>
    /// <value>The status description, or <see langword="null"/> when none was supplied.</value>
    public string? StatusDescription { get; }

    /// <summary>Gets the trace identifier.</summary>
    /// <value>The W3C trace identifier.</value>
    public ActivityTraceId TraceId { get; }

    /// <summary>Gets the span identifier.</summary>
    /// <value>The W3C span identifier.</value>
    public ActivitySpanId SpanId { get; }

    /// <summary>Gets the parent span identifier.</summary>
    /// <value>The parent span identifier, or its default value when the activity has no parent.</value>
    public ActivitySpanId ParentSpanId { get; }

    /// <summary>Gets the activity start time.</summary>
    /// <value>The absolute UTC instant at which the activity started.</value>
    public DateTimeOffset StartTime { get; }

    /// <summary>Gets the completed activity duration.</summary>
    /// <value>The elapsed time from activity start through stop.</value>
    public TimeSpan Duration { get; }

    /// <summary>Gets a stable copy of the activity tags.</summary>
    /// <value>
    /// The retained read-only tag dictionary. Values are not deep-cloned or redacted and may be
    /// sensitive or mutable.
    /// </value>
    public IReadOnlyDictionary<string, object?> Tags { get; }

    /// <summary>Gets a stable copy of the activity baggage.</summary>
    /// <value>The retained read-only baggage dictionary. Values are not redacted and may be sensitive.</value>
    public IReadOnlyDictionary<string, string?> Baggage { get; }

    /// <summary>Gets a stable copy of the activity events.</summary>
    /// <value>The retained event list in recording order. Event tag values are not redacted.</value>
    public IReadOnlyList<TestActivityEvent> Events { get; }
}
