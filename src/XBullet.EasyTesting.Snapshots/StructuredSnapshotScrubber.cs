using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Snapshots;

internal static class StructuredSnapshotScrubber
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    private static readonly string[] RoundTripDateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    public static string Apply(string serialized, SnapshotSettings settings)
    {
        if (settings.ScrubbedMembers.Count == 0 &&
            settings.IgnoredMembers.Count == 0 &&
            !settings.ScrubGuidValues &&
            !settings.ScrubDateTimeValues &&
            settings.PathRules.Count == 0 &&
            !settings.CanonicalizeObjectProperties)
        {
            return serialized;
        }

        JsonNode? root = JsonNode.Parse(serialized);
        ScrubNode(root, settings);
        foreach (var rule in settings.PathRules)
        {
            root = ApplyPathRule(root, rule, settings);
        }

        if (settings.CanonicalizeObjectProperties)
        {
            root = Canonicalize(root);
        }

        return root?.ToJsonString(settings.JsonSerializerOptions) ?? "null";
    }

    private static JsonNode? ApplyPathRule(
        JsonNode? root,
        JsonSnapshotPathRule rule,
        SnapshotSettings settings)
    {
        var locations = FindLocations(root, rule.Path);
        if (rule.Kind == JsonSnapshotPathRuleKind.Ignore)
        {
            return RemoveLocations(root, locations);
        }

        foreach (var location in locations)
        {
            if (rule.Kind == JsonSnapshotPathRuleKind.Scrub)
            {
                root = ReplaceLocation(root, location, JsonValue.Create("{Scrubbed}"));
            }
            else if (rule.Kind == JsonSnapshotPathRuleKind.Replace)
            {
                root = ReplaceLocation(
                    root,
                    location,
                    SerializeReplacement(rule.Replacement, settings.JsonSerializerOptions));
            }
            else if (rule.Kind == JsonSnapshotPathRuleKind.Hash)
            {
                root = ReplaceLocation(root, location, JsonValue.Create(Hash(location.Value)));
            }
            else
            {
                SortArray(location, rule);
            }
        }

        return root;
    }

    private static IReadOnlyList<NodeLocation> FindLocations(JsonNode? root, JsonSnapshotPath path)
    {
        var locations = new List<NodeLocation>();
        FindLocations(
            root,
            parent: null,
            propertyName: null,
            arrayIndex: null,
            path.Segments,
            segmentIndex: 0,
            locations);
        return locations;
    }

    private static void FindLocations(
        JsonNode? node,
        JsonNode? parent,
        string? propertyName,
        int? arrayIndex,
        IReadOnlyList<JsonSnapshotPathSegment> segments,
        int segmentIndex,
        ICollection<NodeLocation> locations)
    {
        if (segmentIndex == segments.Count)
        {
            locations.Add(new NodeLocation(parent, propertyName, arrayIndex, node));
            return;
        }

        if (node is null)
        {
            return;
        }

        var segment = segments[segmentIndex];
        switch (node)
        {
            case JsonObject jsonObject when segment.IsWildcard:
                foreach (var property in jsonObject.ToArray())
                {
                    FindLocations(
                        property.Value,
                        jsonObject,
                        property.Key,
                        arrayIndex: null,
                        segments,
                        segmentIndex + 1,
                        locations);
                }

                break;
            case JsonObject jsonObject when jsonObject.TryGetPropertyValue(segment.Value, out var value):
                FindLocations(
                    value,
                    jsonObject,
                    segment.Value,
                    arrayIndex: null,
                    segments,
                    segmentIndex + 1,
                    locations);
                break;
            case JsonArray jsonArray when segment.IsWildcard:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    FindLocations(
                        jsonArray[index],
                        jsonArray,
                        propertyName: null,
                        index,
                        segments,
                        segmentIndex + 1,
                        locations);
                }

                break;
            case JsonArray jsonArray when
                int.TryParse(segment.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 &&
                index < jsonArray.Count:
                FindLocations(
                    jsonArray[index],
                    jsonArray,
                    propertyName: null,
                    index,
                    segments,
                    segmentIndex + 1,
                    locations);
                break;
        }
    }

    private static JsonNode? RemoveLocations(
        JsonNode? root,
        IReadOnlyList<NodeLocation> locations)
    {
        if (locations.Any(location => location.Parent is null))
        {
            return null;
        }

        foreach (var location in locations.Where(location => location.Parent is JsonObject))
        {
            ((JsonObject)location.Parent!).Remove(location.PropertyName!);
        }

        foreach (var group in locations
                     .Where(location => location.Parent is JsonArray)
                     .GroupBy<NodeLocation, JsonArray>(
                         location => (JsonArray)location.Parent!,
                         ReferenceEqualityComparer.Instance))
        {
            var array = group.Key;
            foreach (var location in group.OrderByDescending(location => location.ArrayIndex))
            {
                array.RemoveAt(location.ArrayIndex!.Value);
            }
        }

        return root;
    }

    private static JsonNode? ReplaceLocation(
        JsonNode? root,
        NodeLocation location,
        JsonNode? replacement)
    {
        if (location.Parent is null)
        {
            return replacement;
        }

        if (location.Parent is JsonObject jsonObject)
        {
            jsonObject[location.PropertyName!] = replacement;
        }
        else
        {
            ((JsonArray)location.Parent)[location.ArrayIndex!.Value] = replacement;
        }

        return root;
    }

    private static JsonNode? SerializeReplacement(
        object? replacement,
        JsonSerializerOptions serializerOptions) =>
        replacement is null
            ? null
            : JsonSerializer.SerializeToNode(
                replacement,
                replacement.GetType(),
                serializerOptions);

    private static string Hash(JsonNode? value)
    {
        var canonical = Canonicalize(value);
        var json = canonical?.ToJsonString(CompactJsonOptions) ?? "null";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static void SortArray(NodeLocation location, JsonSnapshotPathRule rule)
    {
        if (location.Value is not JsonArray array)
        {
            throw new InvalidOperationException(
                $"Snapshot path '{rule.Path.Value}' must select an array to be sorted.");
        }

        var sorted = array
            .Select((item, index) => new SortableArrayItem(
                item,
                index,
                GetSortKey(item, rule.Path, rule.SortItemPath)))
            .OrderBy(item => item.SortKey, StringComparer.Ordinal)
            .ThenBy(item => item.OriginalIndex)
            .ToArray();

        array.Clear();
        foreach (var item in sorted)
        {
            array.Add(item.Value);
        }
    }

    private static string GetSortKey(
        JsonNode? item,
        JsonSnapshotPath arrayPath,
        JsonSnapshotPath? itemPath)
    {
        var key = item;
        if (itemPath is not null)
        {
            var locations = FindLocations(item, itemPath);
            if (locations.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Array item at snapshot path '{arrayPath.Value}' does not contain sort key " +
                    $"'{itemPath.Value}'.");
            }

            key = locations[0].Value;
        }

        var canonical = Canonicalize(key);
        return canonical?.ToJsonString(CompactJsonOptions) ?? "null";
    }

    private static JsonNode? Canonicalize(JsonNode? node) =>
        node switch
        {
            JsonObject jsonObject => new JsonObject(
                jsonObject
                    .OrderBy(property => property.Key, StringComparer.Ordinal)
                    .Select(property => KeyValuePair.Create(
                        property.Key,
                        Canonicalize(property.Value)))),
            JsonArray jsonArray => new JsonArray(
                jsonArray.Select(Canonicalize).ToArray()),
            null => null,
            _ => node.DeepClone()
        };

    private static void ScrubNode(JsonNode? node, SnapshotSettings settings)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                ScrubObject(jsonObject, settings);
                break;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var item = jsonArray[index];
                    if (TryScrubValue(item, settings, out var replacement))
                    {
                        jsonArray[index] = replacement;
                    }
                    else
                    {
                        ScrubNode(item, settings);
                    }
                }

                break;
        }
    }

    private static void ScrubObject(JsonObject jsonObject, SnapshotSettings settings)
    {
        foreach (var property in jsonObject.ToArray())
        {
            if (settings.IgnoredMembers.Contains(property.Key))
            {
                jsonObject.Remove(property.Key);
                continue;
            }

            if (settings.ScrubbedMembers.Contains(property.Key))
            {
                jsonObject[property.Key] = "{Scrubbed}";
                continue;
            }

            if (TryScrubValue(property.Value, settings, out var replacement))
            {
                jsonObject[property.Key] = replacement;
            }
            else
            {
                ScrubNode(property.Value, settings);
            }
        }
    }

    private static bool TryScrubValue(
        JsonNode? node,
        SnapshotSettings settings,
        out string? replacement)
    {
        replacement = null;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var stringValue))
        {
            return false;
        }

        if (settings.ScrubGuidValues && Guid.TryParse(stringValue, out _))
        {
            replacement = "{Guid}";
            return true;
        }

        if (settings.ScrubDateTimeValues &&
            DateTimeOffset.TryParseExact(
                stringValue,
                RoundTripDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            replacement = "{DateTime}";
            return true;
        }

        return false;
    }

    private sealed record NodeLocation(
        JsonNode? Parent,
        string? PropertyName,
        int? ArrayIndex,
        JsonNode? Value);

    private sealed record SortableArrayItem(
        JsonNode? Value,
        int OriginalIndex,
        string SortKey);
}
