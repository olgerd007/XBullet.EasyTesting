using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

internal sealed record SnapshotJsonDifference(
    string Path,
    string Expected,
    string Actual)
{
    private const string Missing = "<missing>";
    private const int MaximumDisplayLength = 200;

    public static SnapshotJsonDifference Find(string expected, string actual)
    {
        try
        {
            using var expectedDocument = JsonDocument.Parse(expected);
            using var actualDocument = JsonDocument.Parse(actual);
            return Compare(expectedDocument.RootElement, actualDocument.RootElement, "$")
                ?? new SnapshotJsonDifference(
                    "$",
                    Describe(expectedDocument.RootElement),
                    Describe(actualDocument.RootElement));
        }
        catch (JsonException)
        {
            return new SnapshotJsonDifference("$", Describe(expected), Describe(actual));
        }
    }

    public static SnapshotJsonDifference MissingExpected(string actual)
    {
        try
        {
            using var document = JsonDocument.Parse(actual);
            return new SnapshotJsonDifference("$", Missing, Describe(document.RootElement));
        }
        catch (JsonException)
        {
            return new SnapshotJsonDifference("$", Missing, Describe(actual));
        }
    }

    private static SnapshotJsonDifference? Compare(
        JsonElement expected,
        JsonElement actual,
        string path)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return Difference(path, expected, actual);
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                return CompareObjects(expected, actual, path);
            case JsonValueKind.Array:
                return CompareArrays(expected, actual, path);
            case JsonValueKind.String:
                return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal)
                    ? null
                    : Difference(path, expected, actual);
            case JsonValueKind.Number:
                return string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal)
                    ? null
                    : Difference(path, expected, actual);
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return Difference(path, expected, actual);
        }
    }

    private static SnapshotJsonDifference? CompareObjects(
        JsonElement expected,
        JsonElement actual,
        string path)
    {
        var expectedProperties = expected.EnumerateObject().ToArray();
        var actualProperties = actual.EnumerateObject().ToArray();
        var sharedCount = Math.Min(expectedProperties.Length, actualProperties.Length);

        for (var index = 0; index < sharedCount; index++)
        {
            var expectedProperty = expectedProperties[index];
            var actualProperty = actualProperties[index];
            if (!string.Equals(expectedProperty.Name, actualProperty.Name, StringComparison.Ordinal))
            {
                return new SnapshotJsonDifference(
                    path,
                    $"property {JsonSerializer.Serialize(expectedProperty.Name)}",
                    $"property {JsonSerializer.Serialize(actualProperty.Name)}");
            }

            var difference = Compare(
                expectedProperty.Value,
                actualProperty.Value,
                AppendProperty(path, expectedProperty.Name));
            if (difference is not null)
            {
                return difference;
            }
        }

        if (expectedProperties.Length > sharedCount)
        {
            var property = expectedProperties[sharedCount];
            return new SnapshotJsonDifference(
                AppendProperty(path, property.Name),
                Describe(property.Value),
                Missing);
        }

        if (actualProperties.Length > sharedCount)
        {
            var property = actualProperties[sharedCount];
            return new SnapshotJsonDifference(
                AppendProperty(path, property.Name),
                Missing,
                Describe(property.Value));
        }

        return null;
    }

    private static SnapshotJsonDifference? CompareArrays(
        JsonElement expected,
        JsonElement actual,
        string path)
    {
        var expectedItems = expected.EnumerateArray().ToArray();
        var actualItems = actual.EnumerateArray().ToArray();
        var sharedCount = Math.Min(expectedItems.Length, actualItems.Length);

        for (var index = 0; index < sharedCount; index++)
        {
            var difference = Compare(expectedItems[index], actualItems[index], $"{path}[{index}]");
            if (difference is not null)
            {
                return difference;
            }
        }

        if (expectedItems.Length > sharedCount)
        {
            return new SnapshotJsonDifference(
                $"{path}[{sharedCount}]",
                Describe(expectedItems[sharedCount]),
                Missing);
        }

        if (actualItems.Length > sharedCount)
        {
            return new SnapshotJsonDifference(
                $"{path}[{sharedCount}]",
                Missing,
                Describe(actualItems[sharedCount]));
        }

        return null;
    }

    private static SnapshotJsonDifference Difference(
        string path,
        JsonElement expected,
        JsonElement actual) =>
        new(path, Describe(expected), Describe(actual));

    private static string AppendProperty(string path, string propertyName)
    {
        if (propertyName.Length > 0 &&
            (char.IsLetter(propertyName[0]) || propertyName[0] == '_') &&
            propertyName.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return $"{path}.{propertyName}";
        }

        var escaped = propertyName
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return $"{path}['{escaped}']";
    }

    private static string Describe(JsonElement value) =>
        Describe(JsonSerializer.Serialize(value));

    private static string Describe(string value)
    {
        var singleLine = value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return singleLine.Length <= MaximumDisplayLength
            ? singleLine
            : $"{singleLine[..MaximumDisplayLength]}...";
    }
}
