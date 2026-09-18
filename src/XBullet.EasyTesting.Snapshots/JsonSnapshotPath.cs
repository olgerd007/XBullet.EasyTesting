namespace XBullet.EasyTesting.Snapshots;

internal sealed class JsonSnapshotPath
{
    private JsonSnapshotPath(string value, IReadOnlyList<JsonSnapshotPathSegment> segments)
    {
        Value = value;
        Segments = segments;
    }

    public string Value { get; }

    public IReadOnlyList<JsonSnapshotPathSegment> Segments { get; }

    public static JsonSnapshotPath Parse(string value, bool allowWildcard = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return new JsonSnapshotPath(value, Array.Empty<JsonSnapshotPathSegment>());
        }

        if (value[0] != '/')
        {
            throw new ArgumentException(
                "A JSON snapshot path must be empty for the root or start with '/'.",
                nameof(value));
        }

        var segments = value[1..]
            .Split('/')
            .Select(segment => ParseSegment(segment, allowWildcard, value))
            .ToArray();
        return new JsonSnapshotPath(value, segments);
    }

    private static JsonSnapshotPathSegment ParseSegment(
        string segment,
        bool allowWildcard,
        string path)
    {
        if (segment == "*")
        {
            if (!allowWildcard)
            {
                throw new ArgumentException(
                    "The array sort item path cannot contain wildcards.",
                    nameof(path));
            }

            return new JsonSnapshotPathSegment(string.Empty, IsWildcard: true);
        }

        var decoded = new System.Text.StringBuilder(segment.Length);
        for (var index = 0; index < segment.Length; index++)
        {
            if (segment[index] != '~')
            {
                decoded.Append(segment[index]);
                continue;
            }

            if (++index >= segment.Length)
            {
                throw InvalidEscape(path);
            }

            decoded.Append(segment[index] switch
            {
                '0' => '~',
                '1' => '/',
                '2' => '*',
                _ => throw InvalidEscape(path)
            });
        }

        return new JsonSnapshotPathSegment(decoded.ToString(), IsWildcard: false);
    }

    private static ArgumentException InvalidEscape(string path) =>
        new(
            "JSON snapshot path escapes must use '~0' for '~', '~1' for '/', or '~2' for a literal '*'.",
            nameof(path));
}

internal readonly record struct JsonSnapshotPathSegment(string Value, bool IsWildcard);

internal enum JsonSnapshotPathRuleKind
{
    Scrub,
    Ignore,
    Replace,
    Hash,
    SortArray
}

internal sealed record JsonSnapshotPathRule(
    JsonSnapshotPathRuleKind Kind,
    JsonSnapshotPath Path,
    object? Replacement = null,
    JsonSnapshotPath? SortItemPath = null);
