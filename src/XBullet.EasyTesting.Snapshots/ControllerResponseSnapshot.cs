using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Where(header => !options.IgnoredHeaders.Contains(header.Key))
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);

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

        if (IsJson(content.Headers.ContentType))
        {
            using var document = JsonDocument.Parse(bytes);
            return ToSnapshotValue(document.RootElement);
        }

        if (IsText(content.Headers.ContentType))
        {
            var encoding = GetEncoding(content.Headers.ContentType?.CharSet);
            return encoding.GetString(bytes);
        }

        return new ControllerBinaryBodySnapshot("base64", Convert.ToBase64String(bytes));
    }

    private static object? ToSnapshotValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ToSnapshotValue(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ToSnapshotValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalNumber) => decimalNumber,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };

    private static bool IsJson(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) is true;

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
