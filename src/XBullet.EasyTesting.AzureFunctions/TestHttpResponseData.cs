using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>An in-memory isolated-worker HTTP response.</summary>
public sealed class TestHttpResponseData : HttpResponseData
{
    private readonly TestHttpCookies _cookies = new();

    internal TestHttpResponseData(FunctionContext functionContext)
        : base(functionContext)
    {
    }

    /// <inheritdoc />
    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <inheritdoc />
    public override HttpHeadersCollection Headers { get; set; } = [];

    /// <inheritdoc />
    public override Stream Body { get; set; } = new MemoryStream();

    /// <inheritdoc />
    public override HttpCookies Cookies => _cookies;

    /// <summary>Gets cookies appended by the function.</summary>
    public IReadOnlyList<IHttpCookie> AppendedCookies => _cookies.Items;
}
