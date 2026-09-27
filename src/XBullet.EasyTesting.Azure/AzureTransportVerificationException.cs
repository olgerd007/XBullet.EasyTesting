namespace XBullet.EasyTesting.Azure;

/// <summary>Indicates that recorded Azure pipeline requests did not match an expectation.</summary>
public sealed class AzureTransportVerificationException : Exception
{
    /// <summary>Creates a verification exception.</summary>
    /// <param name="message">The non-null text describing the unmet request expectation.</param>
    public AzureTransportVerificationException(string message)
        : base(message)
    {
    }
}
