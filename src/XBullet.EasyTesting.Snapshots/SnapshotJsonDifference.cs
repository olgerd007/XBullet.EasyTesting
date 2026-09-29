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

    public static SnapshotJsonDifference FindText(string expected, string actual) =>
        new("$text", Describe(expected), Describe(actual));

    public static SnapshotJsonDifference MissingExpectedText(string actual) =>
        new("$text", Missing, Describe(actual));

    private static SnapshotJsonDifference? Compare(
        JsonElement expected,
        JsonElement actual,
        string path)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return Difference(path, expected, actual);
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            return CompareObjects(expected, actual, path);
        }

        if (expected.ValueKind == JsonValueKind.Array)
        {
            return CompareArrays(expected, actual, path);
        }

        if (expected.ValueKind == JsonValueKind.String)
        {
            return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal)
                ? null
                : Difference(path, expected, actual);
        }

        if (expected.ValueKind == JsonValueKind.Number)
        {
            return string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal)
                ? null
                : Difference(path, expected, actual);
        }

        return null;
    }

    private static SnapshotJsonDifference? CompareObjects(
        JsonElement expected,
        JsonElement actual,
        string path)
    {
        var expectedProperties = expected.EnumerateObject();
        var actualProperties = actual.EnumerateObject();
        while (true)
        {
            var hasExpected = expectedProperties.MoveNext();
            var hasActual = actualProperties.MoveNext();
            if (!hasExpected || !hasActual)
            {
                if (hasExpected)
                {
                    var property = expectedProperties.Current;
                    return new SnapshotJsonDifference(
                        AppendProperty(path, property.Name),
                        Describe(property.Value),
                        Missing);
                }

                if (hasActual)
                {
                    var property = actualProperties.Current;
                    return new SnapshotJsonDifference(
                        AppendProperty(path, property.Name),
                        Missing,
                        Describe(property.Value));
                }

                return null;
            }

            var expectedProperty = expectedProperties.Current;
            var actualProperty = actualProperties.Current;
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
    }

    private static SnapshotJsonDifference? CompareArrays(
        JsonElement expected,
        JsonElement actual,
        string path)
    {
        var expectedCount = expected.GetArrayLength();
        var actualCount = actual.GetArrayLength();
        var sharedCount = Math.Min(expectedCount, actualCount);

        for (var index = 0; index < sharedCount; index++)
        {
            var difference = Compare(expected[index], actual[index], $"{path}[{index}]");
            if (difference is not null)
            {
                return difference;
            }
        }

        if (expectedCount > sharedCount)
        {
            return new SnapshotJsonDifference(
                $"{path}[{sharedCount}]",
                Describe(expected[sharedCount]),
                Missing);
        }

        if (actualCount > sharedCount)
        {
            return new SnapshotJsonDifference(
                $"{path}[{sharedCount}]",
                Missing,
                Describe(actual[sharedCount]));
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
        if (IsSimplePropertyName(propertyName))
        {
            return $"{path}.{propertyName}";
        }

        var escaped = propertyName
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return $"{path}['{escaped}']";
    }

    private static bool IsSimplePropertyName(string propertyName)
    {
        if (propertyName.Length == 0 ||
            (!char.IsLetter(propertyName[0]) && propertyName[0] != '_'))
        {
            return false;
        }

        for (var index = 1; index < propertyName.Length; index++)
        {
            if (!char.IsLetterOrDigit(propertyName[index]) && propertyName[index] != '_')
            {
                return false;
            }
        }

        return true;
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
