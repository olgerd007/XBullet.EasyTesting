using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls snapshot naming, storage, serialization, and scrubbing.</summary>
public sealed class SnapshotSettings
{
    private readonly HashSet<string> _scrubbedMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<JsonSnapshotPathRule> _pathRules = new();

    /// <summary>The environment variable used to select automatic snapshot updates.</summary>
    public const string UpdateModeEnvironmentVariable = "INTEGRATION_TESTS_UPDATE_SNAPSHOTS";

    /// <summary>The environment variable that explicitly permits automatic updates in CI.</summary>
    public const string AllowCiUpdatesEnvironmentVariable =
        "INTEGRATION_TESTS_ALLOW_SNAPSHOT_UPDATES_IN_CI";

    /// <summary>Creates snapshot settings using package defaults and environment configuration.</summary>
    public SnapshotSettings()
        : this(readEnvironment: true)
    {
    }

    private SnapshotSettings(bool readEnvironment)
    {
        if (readEnvironment)
        {
            UpdateMode = ReadUpdateMode();
            AllowUpdatesInContinuousIntegration =
                ContinuousIntegrationEnvironment.IsEnabled(AllowCiUpdatesEnvironmentVariable);
        }
    }

    /// <summary>
    /// Gets or sets the snapshot directory. Relative paths are resolved from the calling source file.
    /// The default is a <c>__snapshots__</c> directory beside that source file.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Gets or sets the snapshot name. The calling method name is used when this is not specified.
    /// </summary>
    public string? SnapshotName { get; set; }

    /// <summary>
    /// Gets or sets an optional snapshot variant. Variants create distinct snapshots for multiple
    /// assertions or parameterized cases in the same test method.
    /// </summary>
    public string? Variant { get; set; }

    /// <summary>Gets or sets the JSON options used to serialize the snapshot.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = CreateDefaultJsonOptions();

    /// <summary>Gets transformations applied to serialized content before comparison.</summary>
    public IList<Func<string, string>> Scrubbers { get; } = new List<Func<string, string>>();

    internal IReadOnlySet<string> ScrubbedMembers => _scrubbedMembers;

    internal IReadOnlySet<string> IgnoredMembers => _ignoredMembers;

    internal bool ScrubGuidValues { get; private set; }

    internal bool ScrubDateTimeValues { get; private set; }

    internal IReadOnlyList<JsonSnapshotPathRule> PathRules => _pathRules;

    internal bool CanonicalizeObjectProperties { get; private set; }

    /// <summary>
    /// Gets or sets automatic snapshot-update behavior. The default can be selected with
    /// <c>INTEGRATION_TESTS_UPDATE_SNAPSHOTS=missing</c> or <c>all</c>.
    /// </summary>
    public SnapshotUpdateMode UpdateMode { get; set; }

    /// <summary>Gets or sets whether an installed diff viewer is launched after a mismatch.</summary>
    public bool LaunchDiffTool { get; set; } = true;

    /// <summary>
    /// Gets or sets an explicit diff viewer. When omitted, Visual Studio, VS Code, Rider,
    /// and Meld are discovered automatically. Diff viewers are never launched in CI.
    /// </summary>
    public SnapshotDiffTool? DiffTool { get; set; }

    /// <summary>
    /// Gets or sets whether automatic snapshot updates are allowed when a continuous-integration
    /// environment is detected. The default is controlled by
    /// <c>INTEGRATION_TESTS_ALLOW_SNAPSHOT_UPDATES_IN_CI</c> and is otherwise false.
    /// </summary>
    public bool AllowUpdatesInContinuousIntegration { get; set; }

    /// <summary>Gets or sets an optional instance-scoped catalog that records exercised snapshots.</summary>
    public SnapshotCatalog? Catalog { get; set; }

    /// <summary>Sets the snapshot name and returns this instance.</summary>
    public SnapshotSettings Named(string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        SnapshotName = snapshotName;
        return this;
    }

    /// <summary>Sets a snapshot variant and returns this instance.</summary>
    public SnapshotSettings ForVariant(string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        Variant = variant;
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

    /// <summary>
    /// Replaces values selected by an extended JSON Pointer with <c>{Scrubbed}</c>.
    /// Use an empty path for the root, <c>/</c> separators, and <c>*</c> as a wildcard segment.
    /// </summary>
    public SnapshotSettings ScrubPath(string path)
    {
        _pathRules.Add(new JsonSnapshotPathRule(
            JsonSnapshotPathRuleKind.Scrub,
            JsonSnapshotPath.Parse(path)));
        return this;
    }

    /// <summary>
    /// Removes values selected by an extended JSON Pointer. Use <c>*</c> as a wildcard segment.
    /// </summary>
    public SnapshotSettings IgnorePath(string path)
    {
        _pathRules.Add(new JsonSnapshotPathRule(
            JsonSnapshotPathRuleKind.Ignore,
            JsonSnapshotPath.Parse(path)));
        return this;
    }

    /// <summary>Replaces values selected by an extended JSON Pointer with a serialized value.</summary>
    public SnapshotSettings ReplacePath(string path, object? replacement)
    {
        _pathRules.Add(new JsonSnapshotPathRule(
            JsonSnapshotPathRuleKind.Replace,
            JsonSnapshotPath.Parse(path),
            replacement));
        return this;
    }

    /// <summary>
    /// Replaces values selected by an extended JSON Pointer with a deterministic SHA-256 hash of
    /// their canonical JSON representation.
    /// </summary>
    public SnapshotSettings HashPath(string path)
    {
        _pathRules.Add(new JsonSnapshotPathRule(
            JsonSnapshotPathRuleKind.Hash,
            JsonSnapshotPath.Parse(path)));
        return this;
    }

    /// <summary>
    /// Sorts arrays selected by an extended JSON Pointer. When <paramref name="itemPath"/> is set,
    /// it is resolved relative to each array item and used as the ordinal JSON sort key.
    /// </summary>
    public SnapshotSettings SortArray(string path, string? itemPath = null)
    {
        _pathRules.Add(new JsonSnapshotPathRule(
            JsonSnapshotPathRuleKind.SortArray,
            JsonSnapshotPath.Parse(path),
            SortItemPath: itemPath is null
                ? null
                : JsonSnapshotPath.Parse(itemPath, allowWildcard: false)));
        return this;
    }

    /// <summary>Sorts JSON object properties by ordinal name before comparison.</summary>
    public SnapshotSettings CanonicalizeJson()
    {
        CanonicalizeObjectProperties = true;
        return this;
    }

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

    /// <summary>Explicitly permits automatic snapshot updates in continuous integration.</summary>
    public SnapshotSettings AllowingUpdatesInContinuousIntegration()
    {
        AllowUpdatesInContinuousIntegration = true;
        return this;
    }

    /// <summary>Records matched snapshot paths in an instance-scoped catalog.</summary>
    public SnapshotSettings TrackingWith(SnapshotCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
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

    internal SnapshotSettings Copy()
    {
        var copy = new SnapshotSettings(readEnvironment: false)
        {
            Directory = Directory,
            SnapshotName = SnapshotName,
            Variant = Variant,
            JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerOptions),
            UpdateMode = UpdateMode,
            LaunchDiffTool = LaunchDiffTool,
            DiffTool = DiffTool,
            AllowUpdatesInContinuousIntegration = AllowUpdatesInContinuousIntegration,
            Catalog = Catalog,
            ScrubGuidValues = ScrubGuidValues,
            ScrubDateTimeValues = ScrubDateTimeValues,
            CanonicalizeObjectProperties = CanonicalizeObjectProperties
        };

        foreach (var scrubber in Scrubbers)
        {
            copy.Scrubbers.Add(scrubber);
        }
        copy._scrubbedMembers.UnionWith(_scrubbedMembers);
        copy._ignoredMembers.UnionWith(_ignoredMembers);
        copy._pathRules.AddRange(_pathRules);
        return copy;
    }

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
