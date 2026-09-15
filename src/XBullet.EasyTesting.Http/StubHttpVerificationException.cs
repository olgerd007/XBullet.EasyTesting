namespace XBullet.EasyTesting.Http;

/// <summary>Thrown when recorded outbound HTTP requests do not satisfy a verification.</summary>
public sealed class StubHttpVerificationException : Exception
{
    /// <summary>Creates an outbound HTTP verification failure.</summary>
    public StubHttpVerificationException(string message)
        : base(message)
    {
    }
}
