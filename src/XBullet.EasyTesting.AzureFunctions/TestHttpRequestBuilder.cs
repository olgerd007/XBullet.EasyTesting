using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluently builds an isolated-worker HTTP trigger request.</summary>
public sealed class TestHttpRequestBuilder
{
    private static readonly Uri DefaultBaseAddress = new("http://localhost/");
    private readonly TestFunctionContext _context;
    private readonly HttpHeadersCollection _headers = [];
    private readonly List<ClaimsIdentity> _identities = [];
    private byte[] _body = [];
    private string _method = HttpMethod.Get.Method;
    private Uri _url = DefaultBaseAddress;

    internal TestHttpRequestBuilder(TestFunctionContext context)
    {
        _context = context;
    }

    /// <summary>Sets the HTTP method.</summary>
    /// <param name="method">The non-null HTTP method whose method text is copied.</param>
    /// <returns>This builder, for chaining.</returns>
    public TestHttpRequestBuilder WithMethod(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return WithMethod(method.Method);
    }

    /// <summary>Sets the HTTP method.</summary>
    /// <param name="method">The non-empty method text retained without normalization. The default is <c>GET</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public TestHttpRequestBuilder WithMethod(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        _method = method;
        return this;
    }

    /// <summary>Sets an absolute URL, or a URL relative to <c>http://localhost/</c>.</summary>
    /// <param name="url">The non-empty absolute or relative URL text.</param>
    /// <returns>This builder, for chaining, with relative input resolved against <c>http://localhost/</c>.</returns>
    public TestHttpRequestBuilder WithUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return WithUrl(new Uri(DefaultBaseAddress, url));
    }

    /// <summary>Sets the request URL.</summary>
    /// <param name="url">The non-null absolute or relative URI.</param>
    /// <returns>This builder, for chaining, with relative input resolved against <c>http://localhost/</c>.</returns>
    public TestHttpRequestBuilder WithUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url.IsAbsoluteUri ? url : new Uri(DefaultBaseAddress, url);
        return this;
    }

    /// <summary>Adds a request header.</summary>
    /// <param name="name">The non-empty header name.</param>
    /// <param name="value">The non-null header value; empty text is accepted.</param>
    /// <returns>This builder, for chaining. Repeated calls can add multiple values.</returns>
    public TestHttpRequestBuilder WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers.Add(name, value);
        return this;
    }

    /// <summary>Sets a UTF-8 text body and its content type.</summary>
    /// <param name="value">The non-null text encoded immediately as UTF-8; empty text is accepted.</param>
    /// <param name="contentType">
    /// A non-empty syntactically valid media type. The default is <c>text/plain; charset=utf-8</c>.
    /// Any existing content-type header is replaced.
    /// </param>
    /// <returns>This builder, for chaining, replacing the previous body.</returns>
    public TestHttpRequestBuilder WithTextBody(
        string value,
        string contentType = "text/plain; charset=utf-8")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        _body = Encoding.UTF8.GetBytes(value);
        SetContentType(contentType);
        return this;
    }

    /// <summary>Serializes a value as a JSON request body.</summary>
    /// <typeparam name="T">The value type to serialize.</typeparam>
    /// <param name="value">The value serialized immediately; null is emitted when <typeparamref name="T"/> permits it.</param>
    /// <param name="options">
    /// JSON options to use, or <see langword="null"/> for new web defaults. The content type is set to
    /// <c>application/json; charset=utf-8</c>.
    /// </param>
    /// <returns>This builder, for chaining, replacing the previous body.</returns>
    public TestHttpRequestBuilder WithJsonBody<T>(
        T value,
        JsonSerializerOptions? options = null)
    {
        _body = JsonSerializer.SerializeToUtf8Bytes(
            value,
            options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
        SetContentType("application/json; charset=utf-8");
        return this;
    }

    /// <summary>Adds an authenticated or unauthenticated claims identity.</summary>
    /// <param name="identity">
    /// The non-null identity reference to retain. It is not cloned, so later identity mutations are visible.
    /// </param>
    /// <returns>This builder, for chaining. Identities retain insertion order.</returns>
    public TestHttpRequestBuilder WithIdentity(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identities.Add(identity);
        return this;
    }

    /// <summary>Creates the HTTP request data instance.</summary>
    /// <returns>
    /// A new request with a caller-owned read-only body stream, also captured as the context's
    /// <c>request</c> HTTP-trigger input. Headers and identities reflect the builder's collections;
    /// avoid further builder mutation while using the request.
    /// </returns>
    public HttpRequestData Build()
    {
        var request = new TestHttpRequestData(
            _context,
            _method,
            _url,
            _headers,
            _identities,
            _body);
        _context.WithInputBinding("request", request, "httpTrigger");
        return request;
    }

    private void SetContentType(string value)
    {
        _ = MediaTypeHeaderValue.Parse(value);
        _headers.Remove("Content-Type");
        _headers.Add("Content-Type", value);
    }
}
