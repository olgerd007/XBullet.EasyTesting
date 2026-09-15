namespace XBullet.EasyTesting.Authentication;

/// <summary>Configures end-to-end client-certificate test transport.</summary>
public sealed class TestClientCertificateOptions
{
    /// <summary>Gets or sets whether this scheme becomes the host's default authentication scheme.</summary>
    public bool UseAsDefaultScheme { get; set; }

    /// <summary>Makes the client-certificate scheme the host's default scheme.</summary>
    public TestClientCertificateOptions AsDefaultScheme()
    {
        UseAsDefaultScheme = true;
        return this;
    }
}
