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
    public TestHttpRequestBuilder WithMethod(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return WithMethod(method.Method);
    }

    /// <summary>Sets the HTTP method.</summary>
    public TestHttpRequestBuilder WithMethod(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        _method = method;
        return this;
    }

    /// <summary>Sets an absolute URL, or a URL relative to <c>http://localhost/</c>.</summary>
    public TestHttpRequestBuilder WithUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return WithUrl(new Uri(DefaultBaseAddress, url));
    }

    /// <summary>Sets the request URL.</summary>
    public TestHttpRequestBuilder WithUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url.IsAbsoluteUri ? url : new Uri(DefaultBaseAddress, url);
        return this;
    }

    /// <summary>Adds a request header.</summary>
    public TestHttpRequestBuilder WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers.Add(name, value);
        return this;
    }

    /// <summary>Sets a UTF-8 text body and its content type.</summary>
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
    public TestHttpRequestBuilder WithIdentity(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identities.Add(identity);
        return this;
    }

    /// <summary>Creates the HTTP request data instance.</summary>
    public HttpRequestData Build() =>
        new TestHttpRequestData(
            _context,
            _method,
            _url,
            _headers,
            _identities,
            _body);

    private void SetContentType(string value)
    {
        _ = MediaTypeHeaderValue.Parse(value);
        _headers.Remove("Content-Type");
        _headers.Add("Content-Type", value);
    }
}
