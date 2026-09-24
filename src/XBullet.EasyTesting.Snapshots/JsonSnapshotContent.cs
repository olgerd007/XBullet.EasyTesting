using System.Net.Http.Headers;
using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

internal static class JsonSnapshotContent
{
    public static JsonElement Parse(string json, JsonSerializerOptions serializerOptions)
    {
        using var document = JsonDocument.Parse(json, CreateDocumentOptions(serializerOptions));
        return document.RootElement.Clone();
    }

    public static JsonElement Parse(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        return document.RootElement.Clone();
    }

    public static async Task<JsonElement> ParseAsync(
        Stream utf8Json,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            utf8Json,
            CreateDocumentOptions(serializerOptions),
            cancellationToken);
        return document.RootElement.Clone();
    }

    public static bool IsJson(MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType;
        return mediaType is not null &&
            (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
             mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonDocumentOptions CreateDocumentOptions(JsonSerializerOptions options) => new()
    {
        AllowTrailingCommas = options.AllowTrailingCommas,
        CommentHandling = options.ReadCommentHandling,
        MaxDepth = options.MaxDepth
    };
}
