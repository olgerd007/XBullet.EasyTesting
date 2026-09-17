namespace XBullet.EasyTesting.Azure;

/// <summary>Indicates that recorded Azure pipeline requests did not match an expectation.</summary>
public sealed class AzureTransportVerificationException : Exception
{
    /// <summary>Creates a verification exception.</summary>
    public AzureTransportVerificationException(string message)
        : base(message)
    {
    }
}
