namespace XBullet.EasyTesting.Http;

/// <summary>Thrown when recorded outbound HTTP requests do not satisfy a verification.</summary>
public sealed class StubHttpVerificationException : Exception
{
    /// <summary>Creates an outbound HTTP verification failure.</summary>
    /// <param name="message">
    /// The non-null diagnostic message describing the expected and actual request counts. Callers
    /// constructing this exception are responsible for redacting sensitive values.
    /// </param>
    public StubHttpVerificationException(string message)
        : base(message)
    {
    }
}
