using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>A stable representation of one captured outbound HTTP exchange.</summary>
public sealed class StubHttpExchangeSnapshot
{
    private StubHttpExchangeSnapshot(
        StubHttpRequestSnapshot request,
        StubHttpResponseSnapshot? response,
        StubHttpFailureSnapshot? failure)
    {
        Request = request;
        Response = response;
        Failure = failure;
    }

    /// <summary>Gets the captured request.</summary>
    public StubHttpRequestSnapshot Request { get; }

    /// <summary>Gets the captured response, when one was produced.</summary>
    public StubHttpResponseSnapshot? Response { get; }

    /// <summary>Gets the send failure, when no response was produced.</summary>
    public StubHttpFailureSnapshot? Failure { get; }

    /// <summary>Creates a deterministic snapshot model from a captured exchange.</summary>
    public static StubHttpExchangeSnapshot FromExchange(
        StubHttpExchange exchange,
        StubHttpExchangeSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        options ??= new StubHttpExchangeSnapshotOptions();

        return new StubHttpExchangeSnapshot(
            StubHttpRequestSnapshot.FromRequest(exchange.Request, options.Request),
            exchange.Response is null
                ? null
                : StubHttpResponseSnapshot.FromResponse(exchange.Response, options.Response),
            exchange.Failure is null
                ? null
                : new StubHttpFailureSnapshot(exchange.Failure.Type, exchange.Failure.Message));
    }
}

/// <summary>Options for request and response portions of an outbound exchange snapshot.</summary>
public sealed class StubHttpExchangeSnapshotOptions
{
    /// <summary>Gets the request snapshot options.</summary>
    public StubHttpRequestSnapshotOptions Request { get; } = new();

    /// <summary>Gets the response snapshot options.</summary>
    public StubHttpResponseSnapshotOptions Response { get; } = new();
}

/// <summary>A stable representation of one captured outbound HTTP response.</summary>
public sealed class StubHttpResponseSnapshot
{
    private StubHttpResponseSnapshot(
        int statusCode,
        string? reasonPhrase,
        IReadOnlyDictionary<string, string[]>? headers,
        object? body,
        StubHttpFailureSnapshot? bodyFailure)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Headers = headers;
        Body = body;
        BodyFailure = bodyFailure;
    }

    /// <summary>Gets the numeric HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the HTTP reason phrase.</summary>
    public string? ReasonPhrase { get; }

    /// <summary>Gets the normalized response headers.</summary>
    public IReadOnlyDictionary<string, string[]>? Headers { get; }

    /// <summary>Gets the normalized response body.</summary>
    public object? Body { get; }

    /// <summary>Gets the failure raised while the response body was read.</summary>
    public StubHttpFailureSnapshot? BodyFailure { get; }

    /// <summary>Creates a deterministic snapshot model from a captured response.</summary>
    public static StubHttpResponseSnapshot FromResponse(
        StubHttpResponse response,
        StubHttpResponseSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        options ??= new StubHttpResponseSnapshotOptions();

        var headers = options.IncludeHeaders
            ? response.Headers
                .Where(header => !options.IgnoredHeaders.Contains(header.Key))
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    header => header.Key,
                    header => options.RedactedHeaders.Contains(header.Key)
                        ? new[] { "{Redacted}" }
                        : header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase)
            : null;
        var body = options.IncludeBody
            ? ReadBody(response)
            : null;

        return new StubHttpResponseSnapshot(
            response.StatusCode,
            response.ReasonPhrase,
            headers,
            body,
            response.BodyFailure is null
                ? null
                : new StubHttpFailureSnapshot(
                    response.BodyFailure.Type,
                    response.BodyFailure.Message));
    }

    private static object? ReadBody(StubHttpResponse response)
    {
        if (!response.BodyCaptured)
        {
            return "{NotRead}";
        }

        if (response.Body.IsEmpty)
        {
            return null;
        }

        var bytes = response.Body.ToArray();
        var contentType = GetContentType(response.Headers);
        if (IsJson(contentType))
        {
            try
            {
                using var document = JsonDocument.Parse(bytes);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return GetEncoding(contentType?.CharSet).GetString(bytes);
            }
        }

        if (IsText(contentType))
        {
            return GetEncoding(contentType?.CharSet).GetString(bytes);
        }

        return new ControllerBinaryBodySnapshot("base64", Convert.ToBase64String(bytes));
    }

    private static MediaTypeHeaderValue? GetContentType(
        IReadOnlyDictionary<string, string[]> headers)
    {
        if (!headers.TryGetValue("Content-Type", out var values))
        {
            return null;
        }

        return values.Select(value =>
            MediaTypeHeaderValue.TryParse(value, out var parsed) ? parsed : null)
            .FirstOrDefault(value => value is not null);
    }

    private static bool IsJson(MediaTypeHeaderValue? contentType) =>
        string.Equals(contentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
        contentType?.MediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) is true;

    private static bool IsText(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) is true ||
        !string.IsNullOrWhiteSpace(contentType?.CharSet);

    private static Encoding GetEncoding(string? charset) =>
        string.IsNullOrWhiteSpace(charset)
            ? Encoding.UTF8
            : Encoding.GetEncoding(charset.Trim('"'));
}

/// <summary>A stable exception description captured in an exchange snapshot.</summary>
public sealed class StubHttpFailureSnapshot
{
    internal StubHttpFailureSnapshot(string type, string message)
    {
        Type = type;
        Message = message;
    }

    /// <summary>Gets the exception type name.</summary>
    public string Type { get; }

    /// <summary>Gets the exception message.</summary>
    public string Message { get; }
}
