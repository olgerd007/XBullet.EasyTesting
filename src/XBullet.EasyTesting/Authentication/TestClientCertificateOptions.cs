namespace XBullet.EasyTesting.Authentication;

/// <summary>Configures end-to-end client-certificate test transport.</summary>
public sealed class TestClientCertificateOptions
{
    /// <summary>Gets or sets whether this scheme becomes the host's default authentication scheme.</summary>
    /// <value>
    /// <see langword="true"/> to make the client-certificate scheme the host default; otherwise,
    /// <see langword="false"/>. The default is <see langword="false"/>.
    /// </value>
    public bool UseAsDefaultScheme { get; set; }

    /// <summary>Makes the client-certificate scheme the host's default scheme.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public TestClientCertificateOptions AsDefaultScheme()
    {
        UseAsDefaultScheme = true;
        return this;
    }
}
