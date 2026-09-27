using Microsoft.Extensions.Logging;

namespace XBullet.EasyTesting.Observability;

/// <summary>One structured log entry captured from an integration-test host.</summary>
public sealed class TestLogEntry
{
    /// <summary>Creates one immutable captured log entry.</summary>
    /// <param name="timestamp">The absolute capture instant supplied by the collector's clock.</param>
    /// <param name="category">The logger category.</param>
    /// <param name="level">The log level.</param>
    /// <param name="eventId">The event identifier and optional name.</param>
    /// <param name="message">The formatted log message.</param>
    /// <param name="messageTemplate">
    /// The original structured message template, or <see langword="null"/> when none was supplied.
    /// </param>
    /// <param name="state">
    /// The structured state. The dictionary is retained without copying and can contain sensitive or
    /// mutable values; callers should not mutate it after construction.
    /// </param>
    /// <param name="scopes">
    /// The active scopes in outer-to-inner order. The list is retained without copying and can contain
    /// sensitive or mutable values; callers should not mutate it after construction.
    /// </param>
    /// <param name="exception">
    /// The logged exception reference, or <see langword="null"/> when none was supplied. The exception
    /// is retained without cloning and remains owned by its creator.
    /// </param>
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
    /// <value>The absolute instant returned by the collector's time provider.</value>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the logger category.</summary>
    /// <value>The category supplied when the logger was created.</value>
    public string Category { get; }

    /// <summary>Gets the log level.</summary>
    /// <value>The captured severity level.</value>
    public LogLevel Level { get; }

    /// <summary>Gets the event identifier.</summary>
    /// <value>The captured numeric event identifier and optional event name.</value>
    public EventId EventId { get; }

    /// <summary>Gets the formatted log message.</summary>
    /// <value>The formatter output. It is not redacted and may contain sensitive data.</value>
    public string Message { get; }

    /// <summary>Gets the original message template when structured logging supplied one.</summary>
    /// <value>The original template, or <see langword="null"/> when structured logging did not supply one.</value>
    public string? MessageTemplate { get; }

    /// <summary>Gets a stable copy of the structured log state.</summary>
    /// <value>The retained read-only state dictionary. Values are not deep-cloned or redacted.</value>
    public IReadOnlyDictionary<string, object?> State { get; }

    /// <summary>Gets a stable copy of the active scopes.</summary>
    /// <value>The retained scopes in outer-to-inner order. Values are not deep-cloned or redacted.</value>
    public IReadOnlyList<object?> Scopes { get; }

    /// <summary>Gets the logged exception, when one was supplied.</summary>
    /// <value>
    /// The original exception reference, or <see langword="null"/> when none was logged. Its message
    /// and data are not redacted.
    /// </value>
    public Exception? Exception { get; }
}
