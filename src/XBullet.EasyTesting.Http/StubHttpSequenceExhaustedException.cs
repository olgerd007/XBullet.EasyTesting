namespace XBullet.EasyTesting.Http;

/// <summary>Thrown when a response sequence receives more calls than it has configured responses.</summary>
public sealed class StubHttpSequenceExhaustedException : InvalidOperationException
{
    /// <summary>Creates an exception for an exhausted response sequence.</summary>
    public StubHttpSequenceExhaustedException(string message)
        : base(message)
    {
    }
}
