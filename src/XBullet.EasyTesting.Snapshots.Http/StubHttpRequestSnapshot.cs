using System.Net.Http.Headers;
using System.Text.Json;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>A stable representation of one captured outbound HTTP request.</summary>
/// <param name="Method">The HTTP method text captured from the request.</param>
/// <param name="Url">
/// The relative path, query, and fragment after configured scrubbing, or
/// <see langword="null"/> when the request had no URI.
/// </param>
/// <param name="Headers">
/// The sorted, copied header snapshot, or <see langword="null"/> when headers were excluded.
/// Ignored headers are absent and redacted values are represented by <c>{Redacted}</c>.
/// </param>
/// <param name="Body">
/// A cloned <see cref="JsonElement"/> for valid JSON content, the original text for other non-empty
/// content, or <see langword="null"/> when the body is absent, empty, or excluded.
/// </param>
public sealed record StubHttpRequestSnapshot(
    string Method,
    string? Url,
    IReadOnlyDictionary<string, string[]>? Headers,
    object? Body)
{
    /// <summary>Gets whether the source request body was truncated during capture.</summary>
    /// <value><see langword="true"/> when trailing request bytes were omitted; otherwise, false.</value>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool BodyTruncated { get; init; }

    /// <summary>Creates a deterministic snapshot model from a captured request.</summary>
    /// <param name="request">
    /// The non-null captured request to read. The method does not own, retain, or mutate it.
    /// </param>
    /// <param name="options">
    /// Inclusion, redaction, and scrubbing settings to read, or <see langword="null"/> to use a new
    /// default options instance. The method does not retain, own, or mutate supplied options;
    /// configured path scrubbers run synchronously before it returns.
    /// </param>
    /// <returns>
    /// A new stable request snapshot that owns its copied headers and normalized body value.
    /// </returns>
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
            SnapshotUrlFormatter.Format(
                request.RequestUri,
                options.RedactedQueryParameters,
                options.ScrubbedQueryParameters,
                options.UrlPathScrubbers),
            headers,
            body)
        {
            BodyTruncated = request.BodyTruncated
        };
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

}
