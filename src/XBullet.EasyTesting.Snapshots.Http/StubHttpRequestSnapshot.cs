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
                    header => header.Value,
                    StringComparer.OrdinalIgnoreCase)
            : null;
        var body = options.IncludeBody
            ? ReadBody(request)
            : null;

        return new StubHttpRequestSnapshot(
            request.Method.Method,
            GetRelativeUrl(request.RequestUri),
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

        var mediaType = parsed.MediaType;
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) is true;
    }

    private static string? GetRelativeUrl(Uri? uri) =>
        uri is null
            ? null
            : uri.IsAbsoluteUri
                ? uri.PathAndQuery
                : uri.OriginalString;
}
