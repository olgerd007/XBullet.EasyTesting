using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently creates a self-signed client certificate for TestServer.</summary>
/// <remarks>
/// This mutable builder is not thread-safe. Its default subject is
/// <c>CN=XBullet.EasyTesting Client</c>; its default validity begins one minute before builder
/// creation and ends one hour after builder creation.
/// </remarks>
public sealed class TestClientCertificateBuilder
{
    private string _subject = "CN=XBullet.EasyTesting Client";
    private DateTimeOffset _notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
    private DateTimeOffset _notAfter = DateTimeOffset.UtcNow.AddHours(1);

    /// <summary>Sets the certificate subject distinguished name.</summary>
    /// <param name="subject">
    /// The non-empty X.500 distinguished name parsed when <see cref="Build"/> runs, for example
    /// <c>CN=integration-client</c>.
    /// </param>
    /// <returns>This builder so additional certificate values can be configured.</returns>
    public TestClientCertificateBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the certificate validity window.</summary>
    /// <param name="notBefore">The absolute instant at which the certificate becomes valid.</param>
    /// <param name="notAfter">
    /// The absolute instant at which the certificate expires. It must be later than
    /// <paramref name="notBefore"/>.
    /// </param>
    /// <returns>This builder so additional certificate values can be configured.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="notAfter"/> is not later than <paramref name="notBefore"/>.
    /// </exception>
    public TestClientCertificateBuilder ValidFrom(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        if (notAfter <= notBefore)
        {
            throw new ArgumentOutOfRangeException(nameof(notAfter));
        }

        _notBefore = notBefore;
        _notAfter = notAfter;
        return this;
    }

    /// <summary>Creates the client certificate.</summary>
    /// <returns>
    /// A new self-signed certificate with a 2048-bit RSA private key, SHA-256 signature, digital
    /// signature key usage, and client-authentication enhanced key usage. The caller owns and must
    /// dispose the certificate.
    /// </returns>
    public X509Certificate2 Build()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            _subject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        var usages = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.2", "Client Authentication")
        };
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(usages, critical: true));
        return request.CreateSelfSigned(_notBefore, _notAfter);
    }
}
