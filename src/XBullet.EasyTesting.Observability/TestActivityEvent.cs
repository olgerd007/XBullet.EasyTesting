namespace XBullet.EasyTesting.Observability;

/// <summary>One event captured from a completed activity.</summary>
public sealed class TestActivityEvent
{
    /// <summary>Creates an immutable captured activity event.</summary>
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
    public string Name { get; }

    /// <summary>Gets the event timestamp.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets a stable copy of the event tags.</summary>
    public IReadOnlyDictionary<string, object?> Tags { get; }
}
