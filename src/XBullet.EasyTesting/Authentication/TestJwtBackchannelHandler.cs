using System.Net;
using System.Net.Http.Json;

namespace XBullet.EasyTesting.Authentication;

internal sealed class TestJwtBackchannelHandler(TestJwtAuthority authority) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;
        authority.RecordBackchannelRequest(path);
        object? body = null;
        if (string.Equals(
            path,
            TestJwtAuthority.NormalizePath(authority.Options.DiscoveryPath),
            StringComparison.OrdinalIgnoreCase))
        {
            body = authority.GetDiscoveryDocument();
        }
        else if (string.Equals(
            path,
            TestJwtAuthority.NormalizePath(authority.Options.JwksPath),
            StringComparison.OrdinalIgnoreCase))
        {
            body = authority.GetJsonWebKeySet();
        }

        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body)
            });
    }
}
