using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <value>The non-null, stable request snapshot owned by this exchange snapshot.</value>
    public StubHttpRequestSnapshot Request { get; }

    /// <summary>Gets the captured response, when one was produced.</summary>
    /// <value>
    /// The stable response snapshot, or <see langword="null"/> when no response existed at snapshot
    /// creation time.
    /// </value>
    public StubHttpResponseSnapshot? Response { get; }

    /// <summary>Gets the send failure, when no response was produced.</summary>
    /// <value>
    /// The stable send-failure snapshot, or <see langword="null"/> when no failure existed at
    /// snapshot creation time. Failure messages are copied without automatic redaction.
    /// </value>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StubHttpFailureSnapshot? Failure { get; }

    /// <summary>Creates a deterministic snapshot model from a captured exchange.</summary>
    /// <param name="exchange">
    /// The non-null exchange whose current request, response, and failure state is copied. The
    /// method does not own, retain, or mutate it; later response-body capture does not change the
    /// returned snapshot.
    /// </param>
    /// <param name="options">
    /// Request, response, and output-format settings to read, or <see langword="null"/> to use a new
    /// default options instance. The format is retained in the options for assertion extensions but
    /// does not alter this in-memory model.
    /// </param>
    /// <returns>A new stable exchange snapshot containing copied and normalized values.</returns>
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
    /// <summary>Gets or sets the committed snapshot file format. The default is JSON.</summary>
    /// <value>
    /// <see cref="HttpExchangeSnapshotFormat.Json"/>, <see cref="HttpExchangeSnapshotFormat.Http"/>,
    /// or <see cref="HttpExchangeSnapshotFormat.Yaml"/>. The default is JSON; unsupported enum
    /// values are rejected when an assertion formats the snapshot.
    /// </value>
    public HttpExchangeSnapshotFormat Format { get; set; } = HttpExchangeSnapshotFormat.Json;

    /// <summary>Gets the request snapshot options.</summary>
    /// <value>
    /// The mutable, options-owned request settings instance. It is created once and cannot be
    /// replaced.
    /// </value>
    public StubHttpRequestSnapshotOptions Request { get; } = new();

    /// <summary>Gets the response snapshot options.</summary>
    /// <value>
    /// The mutable, options-owned response settings instance. It is created once and cannot be
    /// replaced.
    /// </value>
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
        bool bodyTruncated,
        StubHttpFailureSnapshot? bodyFailure)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Headers = headers;
        Body = body;
        BodyTruncated = bodyTruncated;
        BodyFailure = bodyFailure;
    }

    /// <summary>Gets the numeric HTTP status code.</summary>
    /// <value>The integer status code copied from the captured response.</value>
    public int StatusCode { get; }

    /// <summary>Gets the HTTP reason phrase.</summary>
    /// <value>The copied reason phrase, or <see langword="null"/> when none was captured.</value>
    public string? ReasonPhrase { get; }

    /// <summary>Gets the normalized response headers.</summary>
    /// <value>
    /// The sorted, copied header snapshot, or <see langword="null"/> when headers were excluded.
    /// Ignored headers are absent and redacted values are represented by <c>{Redacted}</c>.
    /// </value>
    public IReadOnlyDictionary<string, string[]>? Headers { get; }

    /// <summary>Gets the normalized response body.</summary>
    /// <value>
    /// <c>{NotRead}</c> when content was not consumed; a cloned <see cref="JsonElement"/> for valid
    /// JSON; a string for text or malformed JSON; a base64
    /// <see cref="ControllerBinaryBodySnapshot"/> for binary data; or <see langword="null"/> when
    /// the body is empty or excluded.
    /// </value>
    public object? Body { get; }

    /// <summary>Gets whether the source response body was truncated during capture.</summary>
    /// <value><see langword="true"/> when trailing response bytes were omitted; otherwise, false.</value>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool BodyTruncated { get; }

    /// <summary>Gets the failure raised while the response body was read.</summary>
    /// <value>
    /// The stable content-read failure, or <see langword="null"/> when no failure was captured.
    /// Failure messages are copied without automatic redaction.
    /// </value>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StubHttpFailureSnapshot? BodyFailure { get; }

    /// <summary>Creates a deterministic snapshot model from a captured response.</summary>
    /// <param name="response">
    /// The non-null captured response to read. The method does not own, retain, or mutate it.
    /// </param>
    /// <param name="options">
    /// Header and body inclusion settings to read, or <see langword="null"/> to use a new default
    /// options instance. The method does not retain, own, or mutate supplied options.
    /// </param>
    /// <returns>
    /// A new stable response snapshot that owns its copied headers, normalized body value, and
    /// failure description.
    /// </returns>
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
            response.BodyTruncated,
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

        var contentType = GetContentType(response.Headers);
        if (IsJson(contentType))
        {
            try
            {
                using var document = JsonDocument.Parse(response.Body);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return GetEncoding(contentType!.CharSet).GetString(response.Body.Span);
            }
        }

        if (IsText(contentType))
        {
            return GetEncoding(contentType!.CharSet).GetString(response.Body.Span);
        }

        return new ControllerBinaryBodySnapshot(
            "base64",
            Convert.ToBase64String(response.Body.Span));
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

    private static bool IsJson(MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType;
        return mediaType is not null &&
            (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
             mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsText(MediaTypeHeaderValue? contentType)
    {
        if (contentType is null)
        {
            return false;
        }

        return contentType.MediaType!.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(contentType.CharSet);
    }

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
    /// <value>The captured fully qualified exception type name when available.</value>
    public string Type { get; }

    /// <summary>Gets the exception message.</summary>
    /// <value>The captured message without automatic redaction.</value>
    public string Message { get; }
}
