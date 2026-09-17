using System.Diagnostics;

namespace XBullet.EasyTesting.Observability;

/// <summary>One completed activity captured from an integration-test process.</summary>
public sealed class TestActivityEntry
{
    /// <summary>Creates an immutable captured activity entry.</summary>
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
    public string SourceName { get; }

    /// <summary>Gets the operation name.</summary>
    public string OperationName { get; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the activity kind.</summary>
    public ActivityKind Kind { get; }

    /// <summary>Gets the activity status.</summary>
    public ActivityStatusCode Status { get; }

    /// <summary>Gets the activity status description.</summary>
    public string? StatusDescription { get; }

    /// <summary>Gets the trace identifier.</summary>
    public ActivityTraceId TraceId { get; }

    /// <summary>Gets the span identifier.</summary>
    public ActivitySpanId SpanId { get; }

    /// <summary>Gets the parent span identifier.</summary>
    public ActivitySpanId ParentSpanId { get; }

    /// <summary>Gets the activity start time.</summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>Gets the completed activity duration.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets a stable copy of the activity tags.</summary>
    public IReadOnlyDictionary<string, object?> Tags { get; }

    /// <summary>Gets a stable copy of the activity baggage.</summary>
    public IReadOnlyDictionary<string, string?> Baggage { get; }

    /// <summary>Gets a stable copy of the activity events.</summary>
    public IReadOnlyList<TestActivityEvent> Events { get; }
}
