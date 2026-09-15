using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace XBullet.EasyTesting.Authentication;

internal sealed class TestClientCertificateStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    {
        application.Use(async (context, continuePipeline) =>
        {
            X509Certificate2? certificate = null;
            var encoded = context.Request.Headers[TestAuthenticationDefaults.ClientCertificateHeaderName]
                .FirstOrDefault();
            context.Request.Headers.Remove(TestAuthenticationDefaults.ClientCertificateHeaderName);
            if (!string.IsNullOrWhiteSpace(encoded))
            {
                try
                {
                    var certificateBytes = Convert.FromBase64String(encoded);
#if NET9_0_OR_GREATER
                    certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
#else
                    certificate = new X509Certificate2(certificateBytes);
#endif
                    context.Request.Scheme = "https";
                    context.Connection.ClientCertificate = certificate;
                }
                catch (Exception exception) when (
                    exception is FormatException or System.Security.Cryptography.CryptographicException)
                {
                    // An invalid transport value behaves like a missing client certificate.
                }
            }

            try
            {
                await continuePipeline();
            }
            finally
            {
                certificate?.Dispose();
            }
        });
        next(application);
    };
}
