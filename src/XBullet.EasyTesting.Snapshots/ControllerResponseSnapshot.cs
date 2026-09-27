using System.Net.Http.Headers;
using System.Text;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>A stable representation of an HTTP controller response for snapshot assertions.</summary>
/// <param name="Request">Captured request context, or <see langword="null"/> when request capture is disabled or no request is associated with the response.</param>
/// <param name="StatusCode">Numeric HTTP status code.</param>
/// <param name="ReasonPhrase">HTTP reason phrase, or <see langword="null"/> when none is supplied.</param>
/// <param name="Headers">Captured headers, or an empty case-insensitive dictionary when header capture is disabled.</param>
/// <param name="Body">Captured JSON, text, or base64 binary body, or <see langword="null"/> when body capture is disabled or the body is empty.</param>
public sealed record ControllerResponseSnapshot(
    ControllerRequestSnapshot? Request,
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]> Headers,
    object? Body)
{
    /// <summary>Creates a deterministic snapshot model from an HTTP response.</summary>
    /// <param name="response">The caller-owned, non-null response to capture. It and its request and content are read but not disposed.</param>
    /// <param name="options">Optional capture and redaction options. <see langword="null"/> uses the effective global or default controller options.</param>
    /// <param name="cancellationToken">Token that cancels response-content reading. The default token does not cancel the operation.</param>
    /// <returns>A task whose result is a new deterministic controller-response snapshot with configured redaction applied.</returns>
    public static async Task<ControllerResponseSnapshot> FromResponseAsync(
        HttpResponseMessage response,
        ControllerSnapshotOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        options = ControllerSnapshotOptionsDefaults.MergeGlobalOrDefault(options);

        var request = options.IncludeRequest && response.RequestMessage is not null
            ? new ControllerRequestSnapshot(
                response.RequestMessage.Method.Method,
                SnapshotUrlFormatter.Format(
                    response.RequestMessage.RequestUri,
                    options.RedactedQueryParameters,
                    options.ScrubbedQueryParameters,
                    options.UrlPathScrubbers))
            : null;

        var headers = options.IncludeHeaders
            ? response.Headers
                .Concat(response.Content.Headers)
                .Where(header => !options.IgnoredHeaders.Contains(header.Key))
                .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => options.RedactedHeaders.Contains(group.Key)
                        ? new[] { "{Redacted}" }
                        : group.SelectMany(header => header.Value).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var body = options.IncludeBody
            ? await ReadBodyAsync(response.Content, cancellationToken)
            : null;

        return new ControllerResponseSnapshot(
            request,
            (int)response.StatusCode,
            response.ReasonPhrase,
            headers,
            body);
    }

    private static async Task<object?> ReadBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            return null;
        }

        var contentType = content.Headers.ContentType;
        if (JsonSnapshotContent.IsJson(contentType))
        {
            return JsonSnapshotContent.Parse(bytes);
        }

        if (IsText(contentType))
        {
            var encoding = GetEncoding(contentType!.CharSet);
            return encoding.GetString(bytes);
        }

        return new ControllerBinaryBodySnapshot("base64", Convert.ToBase64String(bytes));
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

    private static Encoding GetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        return Encoding.GetEncoding(charset.Trim('"'));
    }
}

/// <summary>The request context associated with a controller response snapshot.</summary>
/// <param name="Method">HTTP method text, such as <c>GET</c>.</param>
/// <param name="Url">Formatted request URL, or <see langword="null"/> when the request has no URL. Configured query and path redaction has already been applied.</param>
public sealed record ControllerRequestSnapshot(string Method, string? Url);

/// <summary>A binary response body encoded for a text snapshot file.</summary>
/// <param name="Encoding">Name of the text encoding used for <paramref name="Value"/>. Built-in capture produces <c>base64</c>.</param>
/// <param name="Value">Body bytes represented with the named encoding; for <c>base64</c>, this is standard Base64 text.</param>
public sealed record ControllerBinaryBodySnapshot(string Encoding, string Value);
