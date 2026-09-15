using Microsoft.AspNetCore.WebUtilities;

namespace XBullet.EasyTesting.Authentication;

internal sealed class ApiKeyQueryInjectionHandler(string parameterName, string value) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("The HTTP request has no URI.");
        request.RequestUri = new Uri(
            QueryHelpers.AddQueryString(requestUri.ToString(), parameterName, value),
            requestUri.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
        return base.SendAsync(request, cancellationToken);
    }
}
