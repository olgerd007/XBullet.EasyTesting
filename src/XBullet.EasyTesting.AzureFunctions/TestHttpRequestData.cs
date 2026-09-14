using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>An in-memory isolated-worker HTTP request.</summary>
public sealed class TestHttpRequestData : HttpRequestData
{
    private readonly IReadOnlyList<IHttpCookie> _cookies;
    private readonly IReadOnlyList<ClaimsIdentity> _identities;

    internal TestHttpRequestData(
        FunctionContext functionContext,
        string method,
        Uri url,
        HttpHeadersCollection headers,
        IReadOnlyList<ClaimsIdentity> identities,
        byte[] body)
        : base(functionContext)
    {
        Method = method;
        Url = url;
        Headers = headers;
        _identities = identities;
        _cookies = [];
        Body = new MemoryStream(body, writable: false);
    }

    /// <inheritdoc />
    public override Stream Body { get; }

    /// <inheritdoc />
    public override HttpHeadersCollection Headers { get; }

    /// <inheritdoc />
    public override IReadOnlyCollection<IHttpCookie> Cookies => _cookies;

    /// <inheritdoc />
    public override Uri Url { get; }

    /// <inheritdoc />
    public override IEnumerable<ClaimsIdentity> Identities => _identities;

    /// <inheritdoc />
    public override string Method { get; }

    /// <inheritdoc />
    public override HttpResponseData CreateResponse() =>
        new TestHttpResponseData(FunctionContext);
}
