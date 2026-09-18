using System.Net.Http.Headers;
using System.Text;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>A stable representation of an HTTP controller response for snapshot assertions.</summary>
public sealed record ControllerResponseSnapshot(
    ControllerRequestSnapshot? Request,
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]> Headers,
    object? Body)
{
    /// <summary>Creates a deterministic snapshot model from an HTTP response.</summary>
    public static async Task<ControllerResponseSnapshot> FromResponseAsync(
        HttpResponseMessage response,
        ControllerSnapshotOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        options ??= new ControllerSnapshotOptions();

        var request = options.IncludeRequest && response.RequestMessage is not null
            ? new ControllerRequestSnapshot(
                response.RequestMessage.Method.Method,
                GetRelativeUrl(response.RequestMessage.RequestUri))
            : null;

        var headers = options.IncludeHeaders
            ? response.Headers
                .Concat(response.Content.Headers)
                .Where(header => !options.IgnoredHeaders.Contains(header.Key))
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    header => header.Key,
                    header => options.RedactedHeaders.Contains(header.Key)
                        ? new[] { "{Redacted}" }
                        : header.Value.ToArray(),
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

    private static string? GetRelativeUrl(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        return uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
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

        if (JsonSnapshotContent.IsJson(content.Headers.ContentType))
        {
            return JsonSnapshotContent.Parse(bytes);
        }

        if (IsText(content.Headers.ContentType))
        {
            var encoding = GetEncoding(content.Headers.ContentType?.CharSet);
            return encoding.GetString(bytes);
        }

        return new ControllerBinaryBodySnapshot("base64", Convert.ToBase64String(bytes));
    }

    private static bool IsText(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) is true ||
        !string.IsNullOrWhiteSpace(contentType?.CharSet);

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
public sealed record ControllerRequestSnapshot(string Method, string? Url);

/// <summary>A binary response body encoded for a text snapshot file.</summary>
public sealed record ControllerBinaryBodySnapshot(string Encoding, string Value);
