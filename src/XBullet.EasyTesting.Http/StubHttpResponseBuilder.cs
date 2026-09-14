using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.Http;

/// <summary>Defines the response returned by one outbound HTTP request rule.</summary>
public sealed class StubHttpResponseBuilder
{
    private readonly StubHttpMessageHandler _handler;
    private readonly HttpMethod _method;
    private readonly string _requestUri;

    internal StubHttpResponseBuilder(
        StubHttpMessageHandler handler,
        HttpMethod method,
        string requestUri)
    {
        _handler = handler;
        _method = method;
        _requestUri = requestUri;
    }

    /// <summary>Adds a response containing no body.</summary>
    public StubHttpMessageHandler Respond(HttpStatusCode statusCode)
    {
        return _handler.AddRule(
            _method,
            _requestUri,
            () => new HttpResponseMessage(statusCode));
    }

    /// <summary>Adds a JSON response.</summary>
    public StubHttpMessageHandler RespondJson<T>(
        T value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        JsonSerializerOptions? serializerOptions = null)
    {
        return _handler.AddRule(
            _method,
            _requestUri,
            () => new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(value, options: serializerOptions)
            });
    }

    /// <summary>Adds a UTF-8 text response.</summary>
    public StubHttpMessageHandler RespondText(
        string value,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return _handler.AddRule(
            _method,
            _requestUri,
            () => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(value, Encoding.UTF8, mediaType)
            });
    }
}
