using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Http;

internal static class JsonPathMatcher
{
    public static IReadOnlyList<JsonPathSegment> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = new List<JsonPathSegment>();
        var index = 0;
        var hasRootMarker = path[0] == '$';
        if (hasRootMarker)
        {
            index++;
        }

        while (index < path.Length)
        {
            if (path[index] == '.')
            {
                index++;
                AddPropertySegment(path, ref index, segments);
                continue;
            }

            if (path[index] == '[')
            {
                AddArraySegment(path, ref index, segments);
                continue;
            }

            if (segments.Count == 0 && !hasRootMarker)
            {
                AddPropertySegment(path, ref index, segments);
                continue;
            }

            throw InvalidPath(path);
        }

        return segments;
    }

    public static bool Matches(
        string? json,
        IReadOnlyList<JsonPathSegment> path,
        JsonNode? expected)
    {
        return Matches(
            json,
            path,
            element => JsonNode.DeepEquals(JsonNode.Parse(element.GetRawText()), expected));
    }

    public static bool Matches(
        string? json,
        IReadOnlyList<JsonPathSegment> path,
        Func<JsonElement, bool> predicate)
    {
        if (json is null)
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var current = document.RootElement;
            foreach (var segment in path)
            {
                if (segment.PropertyName is not null)
                {
                    if (current.ValueKind != JsonValueKind.Object ||
                        !current.TryGetProperty(segment.PropertyName, out current))
                    {
                        return false;
                    }
                }
                else
                {
                    if (current.ValueKind != JsonValueKind.Array ||
                        segment.ArrayIndex >= current.GetArrayLength())
                    {
                        return false;
                    }

                    current = current.EnumerateArray().ElementAt(segment.ArrayIndex);
                }
            }

            return predicate(current);
        }
    }

    public static IReadOnlyList<JsonPathSegment> RootProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return [JsonPathSegment.ForProperty(propertyName)];
    }

    private static void AddPropertySegment(
        string path,
        ref int index,
        ICollection<JsonPathSegment> segments)
    {
        var start = index;
        while (index < path.Length && path[index] is not ('.' or '['))
        {
            index++;
        }

        if (index == start)
        {
            throw InvalidPath(path);
        }

        segments.Add(JsonPathSegment.ForProperty(path[start..index]));
    }

    private static void AddArraySegment(
        string path,
        ref int index,
        ICollection<JsonPathSegment> segments)
    {
        index++;
        var start = index;
        while (index < path.Length && char.IsAsciiDigit(path[index]))
        {
            index++;
        }

        if (index == start || index >= path.Length || path[index] != ']' ||
            !int.TryParse(
                path.AsSpan(start, index - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var arrayIndex))
        {
            throw InvalidPath(path);
        }

        index++;
        segments.Add(JsonPathSegment.ForArrayIndex(arrayIndex));
    }

    private static ArgumentException InvalidPath(string path) =>
        new(
            $"JSON path '{path}' is invalid. Use property and array-index syntax such as " +
            "'$.customer.id' or '$.items[0].sku'.",
            nameof(path));
}

internal readonly record struct JsonPathSegment(string? PropertyName, int ArrayIndex)
{
    public static JsonPathSegment ForProperty(string propertyName) => new(propertyName, -1);

    public static JsonPathSegment ForArrayIndex(int arrayIndex) => new(null, arrayIndex);
}
