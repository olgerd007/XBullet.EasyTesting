namespace XBullet.EasyTesting.Http;

/// <summary>Thrown when a response sequence receives more calls than it has configured responses.</summary>
public sealed class StubHttpSequenceExhaustedException : InvalidOperationException
{
    /// <summary>Creates an exception for an exhausted response sequence.</summary>
    /// <param name="message">
    /// The non-null diagnostic message identifying the redacted rule URI, configured response
    /// count, and received call number.
    /// </param>
    public StubHttpSequenceExhaustedException(string message)
        : base(message)
    {
    }
}
