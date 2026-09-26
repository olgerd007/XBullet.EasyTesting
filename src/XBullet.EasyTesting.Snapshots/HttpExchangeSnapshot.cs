using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>A stable representation of an HTTP request/response exchange.</summary>
public sealed record HttpExchangeSnapshot(
    HttpExchangeRequestSnapshot? Request,
    HttpExchangeResponseSnapshot? Response,
    HttpExchangeFailureSnapshot? Failure)
{
    /// <summary>Creates a deterministic exchange snapshot from an HTTP response.</summary>
    public static async Task<HttpExchangeSnapshot> FromResponseAsync(
        HttpResponseMessage response,
        HttpExchangeSnapshotOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();
        if (HttpExchangeRecorder.TryGetRecordedSnapshot(response, options, out var recorded))
        {
            return recorded;
        }

        options = HttpExchangeSnapshotOptionsDefaults.MergeGlobalOrDefault(options);

        var request = response.RequestMessage is null
            ? null
            : await CreateRequestAsync(response.RequestMessage, options.Request, cancellationToken);
        var capturedResponse = await CreateResponseAsync(
            response,
            options.Response,
            cancellationToken);

        return new HttpExchangeSnapshot(request, capturedResponse, Failure: null);
    }

    internal static async Task<HttpExchangeRequestSnapshot> CreateRequestAsync(
        HttpRequestMessage request,
        HttpExchangeRequestSnapshotOptions options,
        CancellationToken cancellationToken)
    {
        var headers = options.IncludeHeaders
            ? CaptureHeaders(request.Headers, request.Content?.Headers, options.IgnoredHeaders, options.RedactedHeaders)
            : null;
        var body = options.IncludeBody
            ? await ReadBodyAsync(request.Content, cancellationToken)
            : null;

        return new HttpExchangeRequestSnapshot(
            request.Method.Method,
            SnapshotUrlFormatter.Format(
                request.RequestUri,
                options.RedactedQueryParameters,
                options.ScrubbedQueryParameters,
                options.UrlPathScrubbers),
            headers,
            body);
    }

    internal static async Task<HttpExchangeResponseSnapshot> CreateResponseAsync(
        HttpResponseMessage response,
        HttpExchangeResponseSnapshotOptions options,
        CancellationToken cancellationToken)
    {
        var body = options.IncludeBody
            ? await ReadBodyAsync(response.Content, cancellationToken)
            : null;

        return CreateResponse(response, options, body);
    }

    internal static HttpExchangeResponseSnapshot CreateResponse(
        HttpResponseMessage response,
        HttpExchangeResponseSnapshotOptions options,
        object? body,
        HttpExchangeFailureSnapshot? bodyFailure = null)
    {
        var headers = options.IncludeHeaders
            ? CaptureHeaders(response.Headers, response.Content?.Headers, options.IgnoredHeaders, options.RedactedHeaders)
            : null;

        return new HttpExchangeResponseSnapshot(
            (int)response.StatusCode,
            response.ReasonPhrase,
            headers,
            body)
        {
            BodyFailure = bodyFailure
        };
    }

    internal static object? CreateBodySnapshot(
        ReadOnlyMemory<byte> bytes,
        MediaTypeHeaderValue? contentType)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        if (JsonSnapshotContent.IsJson(contentType))
        {
            return JsonSnapshotContent.Parse(bytes);
        }

        if (IsText(contentType))
        {
            return GetEncoding(contentType!.CharSet).GetString(bytes.Span);
        }

        return new ControllerBinaryBodySnapshot("base64", Convert.ToBase64String(bytes.Span));
    }

    private static IReadOnlyDictionary<string, string[]> CaptureHeaders(
        HttpHeaders headers,
        HttpContentHeaders? contentHeaders,
        ISet<string> ignoredHeaders,
        ISet<string> redactedHeaders) =>
        headers
            .Concat(contentHeaders ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Where(header => !ignoredHeaders.Contains(header.Key))
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => redactedHeaders.Contains(group.Key)
                    ? new[] { "{Redacted}" }
                    : group.SelectMany(header => header.Value).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static async Task<object?> ReadBodyAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        return CreateBodySnapshot(bytes, content.Headers.ContentType);
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

/// <summary>A stable representation of the request in an HTTP exchange.</summary>
public sealed record HttpExchangeRequestSnapshot(
    string Method,
    string? Url,
    IReadOnlyDictionary<string, string[]>? Headers,
    object? Body);

/// <summary>A stable representation of the response in an HTTP exchange.</summary>
public sealed record HttpExchangeResponseSnapshot(
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]>? Headers,
    object? Body)
{
    /// <summary>Gets the failure raised while the response body was recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HttpExchangeFailureSnapshot? BodyFailure { get; init; }
}

/// <summary>A stable representation of a failure that prevented an HTTP response.</summary>
public sealed record HttpExchangeFailureSnapshot(string Type, string Message)
{
    internal static HttpExchangeFailureSnapshot FromException(Exception exception)
    {
        var root = exception.GetBaseException();
        return new HttpExchangeFailureSnapshot(
            root.GetType().FullName!,
            root.Message);
    }
}
