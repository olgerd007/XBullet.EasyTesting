using Microsoft.Extensions.Logging;

namespace XBullet.EasyTesting.Observability;

/// <summary>One structured log entry captured from an integration-test host.</summary>
public sealed class TestLogEntry
{
    /// <summary>Creates one immutable captured log entry.</summary>
    public TestLogEntry(
        DateTimeOffset timestamp,
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        string? messageTemplate,
        IReadOnlyDictionary<string, object?> state,
        IReadOnlyList<object?> scopes,
        Exception? exception)
    {
        Timestamp = timestamp;
        Category = category;
        Level = level;
        EventId = eventId;
        Message = message;
        MessageTemplate = messageTemplate;
        State = state;
        Scopes = scopes;
        Exception = exception;
    }

    /// <summary>Gets the time at which the entry was captured.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the logger category.</summary>
    public string Category { get; }

    /// <summary>Gets the log level.</summary>
    public LogLevel Level { get; }

    /// <summary>Gets the event identifier.</summary>
    public EventId EventId { get; }

    /// <summary>Gets the formatted log message.</summary>
    public string Message { get; }

    /// <summary>Gets the original message template when structured logging supplied one.</summary>
    public string? MessageTemplate { get; }

    /// <summary>Gets a stable copy of the structured log state.</summary>
    public IReadOnlyDictionary<string, object?> State { get; }

    /// <summary>Gets a stable copy of the active scopes.</summary>
    public IReadOnlyList<object?> Scopes { get; }

    /// <summary>Gets the logged exception, when one was supplied.</summary>
    public Exception? Exception { get; }
}
