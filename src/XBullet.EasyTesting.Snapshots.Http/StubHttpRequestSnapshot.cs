using System.Net.Http.Headers;
using System.Text.Json;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>A stable representation of one captured outbound HTTP request.</summary>
public sealed record StubHttpRequestSnapshot(
    string Method,
    string? Url,
    IReadOnlyDictionary<string, string[]>? Headers,
    object? Body)
{
    /// <summary>Creates a deterministic snapshot model from a captured request.</summary>
    public static StubHttpRequestSnapshot FromRequest(
        StubHttpRequest request,
        StubHttpRequestSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= new StubHttpRequestSnapshotOptions();

        var headers = options.IncludeHeaders
            ? request.Headers
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
            ? ReadBody(request)
            : null;

        return new StubHttpRequestSnapshot(
            request.Method.Method,
            GetRelativeUrl(request.RequestUri, options.RedactedQueryParameters),
            headers,
            body);
    }

    private static object? ReadBody(StubHttpRequest request)
    {
        if (string.IsNullOrEmpty(request.Body))
        {
            return null;
        }

        if (!request.Headers.TryGetValue("Content-Type", out var contentTypes) ||
            !contentTypes.Any(IsJsonContentType))
        {
            return request.Body;
        }

        try
        {
            using var document = JsonDocument.Parse(request.Body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return request.Body;
        }
    }

    private static bool IsJsonContentType(string contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        var mediaType = parsed.MediaType!;
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetRelativeUrl(Uri? uri, ISet<string> redactedQueryParameters)
    {
        if (uri is null)
        {
            return null;
        }

        var value = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
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
}
