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
                GetRelativeUrl(
                    response.RequestMessage.RequestUri,
                    options.RedactedQueryParameters))
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

    private static string? GetRelativeUrl(Uri? uri, ISet<string> redactedQueryParameters)
    {
        if (uri is null)
        {
            return null;
        }

        var value = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
        return RedactQueryParameters(value, redactedQueryParameters);
    }

    private static string RedactQueryParameters(string value, ISet<string> redactedQueryParameters)
    {
        var queryIndex = value.IndexOf('?');
        if (queryIndex < 0 || redactedQueryParameters.Count == 0)
        {
            return value;
        }

        var fragmentIndex = value.IndexOf('#', queryIndex + 1);
        var queryEnd = fragmentIndex < 0 ? value.Length : fragmentIndex;
        var query = value[(queryIndex + 1)..queryEnd];
        var redacted = query.Split('&').Select(segment =>
        {
            var equalsIndex = segment.IndexOf('=');
            var encodedName = equalsIndex < 0 ? segment : segment[..equalsIndex];
            var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
            return redactedQueryParameters.Contains(name)
                ? $"{encodedName}={{Redacted}}"
                : segment;
        });

        var fragment = fragmentIndex < 0 ? string.Empty : value[fragmentIndex..];
        return $"{value[..(queryIndex + 1)]}{string.Join('&', redacted)}{fragment}";
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
public sealed record ControllerRequestSnapshot(string Method, string? Url);

/// <summary>A binary response body encoded for a text snapshot file.</summary>
public sealed record ControllerBinaryBodySnapshot(string Encoding, string Value);
