using System.Globalization;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Snapshots;

internal static class StructuredSnapshotScrubber
{
    public static string Apply(string serialized, SnapshotSettings settings)
    {
        if (settings.ScrubbedMembers.Count == 0 &&
            settings.IgnoredMembers.Count == 0 &&
            !settings.ScrubGuidValues &&
            !settings.ScrubDateTimeValues)
        {
            return serialized;
        }

        var root = JsonNode.Parse(serialized);
        ScrubNode(root, settings);
        return root?.ToJsonString(settings.JsonSerializerOptions) ?? "null";
    }

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
            DateTimeOffset.TryParse(
                stringValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            replacement = "{DateTime}";
            return true;
        }

        return false;
    }
}
