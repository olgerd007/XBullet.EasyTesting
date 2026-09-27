namespace XBullet.EasyTesting.Observability;

/// <summary>One event captured from a completed activity.</summary>
public sealed class TestActivityEvent
{
    /// <summary>Creates an immutable captured activity event.</summary>
    /// <param name="name">The event name.</param>
    /// <param name="timestamp">The absolute UTC instant at which the event was recorded.</param>
    /// <param name="tags">
    /// The event tags. The dictionary is retained without copying and can contain sensitive or
    /// mutable values; callers should not mutate it after construction.
    /// </param>
    public TestActivityEvent(
        string name,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, object?> tags)
    {
        Name = name;
        Timestamp = timestamp;
        Tags = tags;
    }

    /// <summary>Gets the event name.</summary>
    /// <value>The event name captured from the activity.</value>
    public string Name { get; }

    /// <summary>Gets the event timestamp.</summary>
    /// <value>The absolute UTC instant at which the event was recorded.</value>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets a stable copy of the event tags.</summary>
    /// <value>The retained read-only tags. Values are not deep-cloned or redacted.</value>
    public IReadOnlyDictionary<string, object?> Tags { get; }
}
