using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently creates a self-signed client certificate for TestServer.</summary>
public sealed class TestClientCertificateBuilder
{
    private string _subject = "CN=XBullet.EasyTesting Client";
    private DateTimeOffset _notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
    private DateTimeOffset _notAfter = DateTimeOffset.UtcNow.AddHours(1);

    /// <summary>Sets the certificate subject distinguished name.</summary>
    public TestClientCertificateBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the certificate validity window.</summary>
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
