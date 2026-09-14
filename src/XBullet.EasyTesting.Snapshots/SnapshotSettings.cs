using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls snapshot naming, storage, serialization, and scrubbing.</summary>
public sealed class SnapshotSettings
{
    private readonly HashSet<string> _scrubbedMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredMembers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The environment variable used to select automatic snapshot updates.</summary>
    public const string UpdateModeEnvironmentVariable = "INTEGRATION_TESTS_UPDATE_SNAPSHOTS";

    /// <summary>
    /// Gets or sets the snapshot directory. Relative paths are resolved from the calling source file.
    /// The default is a <c>__snapshots__</c> directory beside that source file.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Gets or sets the snapshot name. The calling method name is used when this is not specified.
    /// </summary>
    public string? SnapshotName { get; set; }

    /// <summary>Gets or sets the JSON options used to serialize the snapshot.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = CreateDefaultJsonOptions();

    /// <summary>Gets transformations applied to serialized content before comparison.</summary>
    public IList<Func<string, string>> Scrubbers { get; } = new List<Func<string, string>>();

    internal IReadOnlySet<string> ScrubbedMembers => _scrubbedMembers;

    internal IReadOnlySet<string> IgnoredMembers => _ignoredMembers;

    internal bool ScrubGuidValues { get; private set; }

    internal bool ScrubDateTimeValues { get; private set; }

    /// <summary>
    /// Gets or sets automatic snapshot-update behavior. The default can be selected with
    /// <c>INTEGRATION_TESTS_UPDATE_SNAPSHOTS=missing</c> or <c>all</c>.
    /// </summary>
    public SnapshotUpdateMode UpdateMode { get; set; } = ReadUpdateMode();

    /// <summary>Gets or sets whether an installed diff viewer is launched after a mismatch.</summary>
    public bool LaunchDiffTool { get; set; } = true;

    /// <summary>
    /// Gets or sets an explicit diff viewer. When omitted, Visual Studio, VS Code, Rider,
    /// and Meld are discovered automatically. Diff viewers are never launched in CI.
    /// </summary>
    public SnapshotDiffTool? DiffTool { get; set; }

    /// <summary>Sets the snapshot name and returns this instance.</summary>
    public SnapshotSettings Named(string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        SnapshotName = snapshotName;
        return this;
    }

    /// <summary>Sets the snapshot directory and returns this instance.</summary>
    public SnapshotSettings InDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        return this;
    }

    /// <summary>Adds a serialized-content scrubber and returns this instance.</summary>
    public SnapshotSettings Scrub(Func<string, string> scrubber)
    {
        ArgumentNullException.ThrowIfNull(scrubber);
        Scrubbers.Add(scrubber);
        return this;
    }

    /// <summary>
    /// Replaces values of matching JSON members with <c>{Scrubbed}</c> at every nesting level.
    /// Member names are matched without regard to case.
    /// </summary>
    public SnapshotSettings ScrubMembers(params string[] memberNames)
    {
        AddMemberNames(_scrubbedMembers, memberNames);
        return this;
    }

    /// <summary>
    /// Replaces the value of a matching JSON member with <c>{Scrubbed}</c> at every nesting level.
    /// </summary>
    public SnapshotSettings ScrubMember(string memberName) => ScrubMembers(memberName);

    /// <summary>
    /// Removes matching JSON members from the snapshot at every nesting level.
    /// Member names are matched without regard to case.
    /// </summary>
    public SnapshotSettings IgnoreMembers(params string[] memberNames)
    {
        AddMemberNames(_ignoredMembers, memberNames);
        return this;
    }

    /// <summary>Removes a matching JSON member from the snapshot at every nesting level.</summary>
    public SnapshotSettings IgnoreMember(string memberName) => IgnoreMembers(memberName);

    /// <summary>Replaces every JSON string containing a GUID with <c>{Guid}</c>.</summary>
    public SnapshotSettings ScrubGuids()
    {
        ScrubGuidValues = true;
        return this;
    }

    /// <summary>Replaces every round-trip JSON date/time string with <c>{DateTime}</c>.</summary>
    public SnapshotSettings ScrubDateTimes()
    {
        ScrubDateTimeValues = true;
        return this;
    }

    /// <summary>Sets automatic snapshot-update behavior and returns this instance.</summary>
    public SnapshotSettings Updating(SnapshotUpdateMode updateMode)
    {
        UpdateMode = updateMode;
        return this;
    }

    /// <summary>Disables diff-tool launching and returns this instance.</summary>
    public SnapshotSettings WithoutDiffTool()
    {
        LaunchDiffTool = false;
        DiffTool = null;
        return this;
    }

    /// <summary>Selects an explicit diff tool and returns this instance.</summary>
    public SnapshotSettings WithDiffTool(SnapshotDiffTool diffTool)
    {
        ArgumentNullException.ThrowIfNull(diffTool);
        LaunchDiffTool = true;
        DiffTool = diffTool;
        return this;
    }

    private static JsonSerializerOptions CreateDefaultJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    private static void AddMemberNames(ISet<string> target, string[] memberNames)
    {
        ArgumentNullException.ThrowIfNull(memberNames);
        foreach (var memberName in memberNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
            target.Add(memberName);
        }
    }

    private static SnapshotUpdateMode ReadUpdateMode()
    {
        var value = Environment.GetEnvironmentVariable(UpdateModeEnvironmentVariable);
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "none" or "false" or "0" => SnapshotUpdateMode.None,
            "missing" => SnapshotUpdateMode.Missing,
            "all" or "true" or "1" => SnapshotUpdateMode.All,
            _ => throw new InvalidOperationException(
                $"Environment variable {UpdateModeEnvironmentVariable} must be 'none', 'missing', or 'all'.")
        };
    }
}
