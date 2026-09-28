using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls snapshot naming, storage, serialization, and scrubbing.</summary>
public sealed class SnapshotSettings
{
    private static readonly IReadOnlyDictionary<string, SnapshotUpdateMode> UpdateModes =
        new Dictionary<string, SnapshotUpdateMode>(StringComparer.Ordinal)
        {
            ["none"] = SnapshotUpdateMode.None,
            ["false"] = SnapshotUpdateMode.None,
            ["0"] = SnapshotUpdateMode.None,
            ["missing"] = SnapshotUpdateMode.Missing,
            ["all"] = SnapshotUpdateMode.All,
            ["true"] = SnapshotUpdateMode.All,
            ["1"] = SnapshotUpdateMode.All
        };

    private readonly HashSet<string> _scrubbedMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<JsonSnapshotPathRule> _pathRules = new();
    private string? _directory;
    private Func<SnapshotLocationContext, string>? _directoryResolver;
    private string? _snapshotName;
    private string? _variant;
    private JsonSerializerOptions _jsonSerializerOptions = CreateDefaultJsonOptions();
    private SnapshotUpdateMode _updateMode;
    private bool _launchDiffTool = true;
    private SnapshotDiffTool? _diffTool;
    private bool _allowUpdatesInContinuousIntegration;
    private SnapshotCatalog? _catalog;
    private SettingOverrides _overrides;
    private bool _includesGlobalDefaults;

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
            _updateMode = ReadUpdateMode();
            _allowUpdatesInContinuousIntegration =
                ContinuousIntegrationEnvironment.IsEnabled(AllowCiUpdatesEnvironmentVariable);
        }
    }

    /// <summary>
    /// Gets or sets the snapshot directory. Relative paths are resolved from the calling source file.
    /// The default is a <c>__snapshots__</c> directory beside that source file.
    /// </summary>
    /// <value>
    /// An absolute path or a path relative to the caller's source directory, or <see langword="null"/>
    /// to use the resolver or the default sibling <c>__snapshots__</c> directory.
    /// </value>
    public string? Directory
    {
        get => _directory;
        set
        {
            _directory = value;
            _overrides |= SettingOverrides.Directory;
        }
    }
    /// <summary>
    /// Gets or sets an optional callback that selects the snapshot directory from the calling test
    /// context. <see cref="Directory"/> takes precedence when both are set.
    /// </summary>
    /// <value>
    /// A callback retained and invoked during path resolution, or <see langword="null"/>. It must
    /// return a non-empty absolute or source-relative directory. <see cref="Directory"/> takes precedence.
    /// </value>
    public Func<SnapshotLocationContext, string>? DirectoryResolver
    {
        get => _directoryResolver;
        set
        {
            _directoryResolver = value;
            _overrides |= SettingOverrides.DirectoryResolver;
        }
    }
    /// <summary>
    /// Gets or sets the snapshot name. The calling method name is used when this is not specified.
    /// </summary>
    /// <value>A filename component, or <see langword="null"/> to use the caller member name.</value>
    public string? SnapshotName
    {
        get => _snapshotName;
        set
        {
            _snapshotName = value;
            _overrides |= SettingOverrides.SnapshotName;
        }
    }
    /// <summary>
    /// Gets or sets an optional snapshot variant. Variants create distinct snapshots for multiple
    /// assertions or parameterized cases in the same test method.
    /// </summary>
    /// <value>An optional filename component distinguishing snapshots from the same assertion.</value>
    public string? Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            _overrides |= SettingOverrides.Variant;
        }
    }
    /// <summary>Gets or sets the JSON options used to serialize the snapshot.</summary>
    /// <value>
    /// The mutable serializer options used immediately by assertions. The default uses web defaults,
    /// indented output, and original property names. Defaults templates clone this object.
    /// </value>
    public JsonSerializerOptions JsonSerializerOptions
    {
        get
        {
            _overrides |= SettingOverrides.JsonSerializerOptions;
            return _jsonSerializerOptions;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _jsonSerializerOptions = value;
            _overrides |= SettingOverrides.JsonSerializerOptions;
        }
    }
    /// <summary>Gets transformations applied to serialized content before comparison.</summary>
    /// <value>
    /// The live ordered callback list. Scrubbers run after structural JSON rules and must return
    /// non-null content; JSON scrubbers must leave valid JSON.
    /// </value>
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
    /// <value>
    /// <see cref="SnapshotUpdateMode.None"/> to write received files and fail,
    /// <see cref="SnapshotUpdateMode.Missing"/> to approve only missing files, or
    /// <see cref="SnapshotUpdateMode.All"/> to overwrite missing and changed verified files.
    /// </value>
    public SnapshotUpdateMode UpdateMode
    {
        get => _updateMode;
        set
        {
            _updateMode = value;
            _overrides |= SettingOverrides.UpdateMode;
        }
    }
    /// <summary>Gets or sets whether an installed diff viewer is launched after a mismatch.</summary>
    /// <value>
    /// <see langword="true"/> to attempt launching a viewer after mismatches outside CI; otherwise,
    /// <see langword="false"/>. The default is true.
    /// </value>
    public bool LaunchDiffTool
    {
        get => _launchDiffTool;
        set
        {
            _launchDiffTool = value;
            _overrides |= SettingOverrides.LaunchDiffTool;
        }
    }
    /// <summary>
    /// Gets or sets an explicit diff viewer. When omitted, Visual Studio, VS Code, Rider,
    /// and Meld are discovered automatically. Diff viewers are never launched in CI.
    /// </summary>
    /// <value>An explicit retained configuration, or <see langword="null"/> to auto-discover a supported viewer.</value>
    public SnapshotDiffTool? DiffTool
    {
        get => _diffTool;
        set
        {
            _diffTool = value;
            _overrides |= SettingOverrides.DiffTool;
        }
    }
    /// <summary>
    /// Gets or sets whether automatic snapshot updates are allowed when a continuous-integration
    /// environment is detected. The default is controlled by
    /// <c>INTEGRATION_TESTS_ALLOW_SNAPSHOT_UPDATES_IN_CI</c> and is otherwise false.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to permit configured automatic writes in detected CI environments;
    /// otherwise, <see langword="false"/>. This does not itself enable updates.
    /// </value>
    public bool AllowUpdatesInContinuousIntegration
    {
        get => _allowUpdatesInContinuousIntegration;
        set
        {
            _allowUpdatesInContinuousIntegration = value;
            _overrides |= SettingOverrides.AllowUpdatesInContinuousIntegration;
        }
    }
    /// <summary>Gets or sets an optional instance-scoped catalog that records exercised snapshots.</summary>
    /// <value>The caller-owned catalog notified of verified paths, or <see langword="null"/> for no tracking.</value>
    public SnapshotCatalog? Catalog
    {
        get => _catalog;
        set
        {
            _catalog = value;
            _overrides |= SettingOverrides.Catalog;
        }
    }
    /// <summary>Sets the snapshot name and returns this instance.</summary>
    /// <param name="snapshotName">The non-empty name used as a sanitized filename component.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings Named(string snapshotName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        SnapshotName = snapshotName;
        return this;
    }

    /// <summary>Sets a snapshot variant and returns this instance.</summary>
    /// <param name="variant">The non-empty variant used as a sanitized filename component.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings ForVariant(string variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        Variant = variant;
        return this;
    }

    /// <summary>
    /// Creates a compact deterministic variant from serialized parameter values and returns this
    /// instance. This is useful when parameter text would produce an excessively long filename.
    /// </summary>
    /// <param name="values">The non-null parameter array serialized immediately with current JSON options.</param>
    /// <returns>
    /// This settings instance with a <c>hash-</c> variant followed by the first 16 lowercase SHA-256
    /// hexadecimal characters of the serialized values.
    /// </returns>
    public SnapshotSettings ForHashedVariant(params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var serialized = JsonSerializer.Serialize(values, JsonSerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return ForVariant($"hash-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}");
    }

    /// <summary>Sets the snapshot directory and returns this instance.</summary>
    /// <param name="directory">A non-empty absolute path or path relative to the caller source directory.</param>
    /// <returns>This settings instance, for chaining, with any directory resolver cleared.</returns>
    public SnapshotSettings InDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        DirectoryResolver = null;
        return this;
    }

    /// <summary>Sets a context-aware snapshot-directory resolver and returns this instance.</summary>
    /// <param name="directoryResolver">
    /// A non-null callback retained until assertion and required to return a non-empty directory path.
    /// </param>
    /// <returns>This settings instance, for chaining, with any fixed directory cleared.</returns>
    public SnapshotSettings InDirectory(Func<SnapshotLocationContext, string> directoryResolver)
    {
        ArgumentNullException.ThrowIfNull(directoryResolver);
        Directory = null;
        DirectoryResolver = directoryResolver;
        return this;
    }

    /// <summary>
    /// Stores snapshots directly beside the calling source file instead of in its default
    /// <c>__snapshots__</c> directory.
    /// </summary>
    /// <returns>This settings instance, for chaining, with directory <c>.</c>.</returns>
    public SnapshotSettings BesideSourceFile() => InDirectory(".");

    /// <summary>Adds a serialized-content scrubber and returns this instance.</summary>
    /// <param name="scrubber">A non-null callback retained and run in registration order before comparison.</param>
    /// <returns>This settings instance, for chaining.</returns>
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
    /// <param name="memberNames">A non-null array of non-empty names; duplicates are ignored case-insensitively.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings ScrubMembers(params string[] memberNames)
    {
        AddMemberNames(_scrubbedMembers, memberNames);
        return this;
    }

    /// <summary>
    /// Replaces the value of a matching JSON member with <c>{Scrubbed}</c> at every nesting level.
    /// </summary>
    /// <param name="memberName">The non-empty case-insensitive member name.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings ScrubMember(string memberName) => ScrubMembers(memberName);

    /// <summary>
    /// Removes matching JSON members from the snapshot at every nesting level.
    /// Member names are matched without regard to case.
    /// </summary>
    /// <param name="memberNames">A non-null array of non-empty names; duplicates are ignored case-insensitively.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings IgnoreMembers(params string[] memberNames)
    {
        AddMemberNames(_ignoredMembers, memberNames);
        return this;
    }

    /// <summary>Removes a matching JSON member from the snapshot at every nesting level.</summary>
    /// <param name="memberName">The non-empty case-insensitive member name.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings IgnoreMember(string memberName) => IgnoreMembers(memberName);

    /// <summary>
    /// Replaces values selected by an extended JSON Pointer with <c>{Scrubbed}</c>.
    /// Use an empty path for the root, <c>/</c> separators, and <c>*</c> as a wildcard segment.
    /// </summary>
    /// <param name="path">The non-null extended JSON Pointer; an empty string selects the root.</param>
    /// <returns>This settings instance, for chaining. Rules run in registration order.</returns>
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
    /// <param name="path">The non-null extended JSON Pointer; an empty string selects the root.</param>
    /// <returns>This settings instance, for chaining. Rules run in registration order.</returns>
    public SnapshotSettings IgnorePath(string path)
    {
        _pathRules.Add(new JsonSnapshotPathRule(
            JsonSnapshotPathRuleKind.Ignore,
            JsonSnapshotPath.Parse(path)));
        return this;
    }

    /// <summary>Replaces values selected by an extended JSON Pointer with a serialized value.</summary>
    /// <param name="path">The non-null extended JSON Pointer; an empty string selects the root.</param>
    /// <param name="replacement">The replacement serialized with current JSON options; null is accepted.</param>
    /// <returns>This settings instance, for chaining. Rules run in registration order.</returns>
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
    /// <param name="path">The non-null extended JSON Pointer; an empty string selects the root.</param>
    /// <returns>This settings instance, for chaining. Rules run in registration order.</returns>
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
    /// <param name="path">The non-null extended JSON Pointer selecting arrays; wildcard segments are accepted.</param>
    /// <param name="itemPath">
    /// An optional non-wildcard JSON Pointer relative to each item, or <see langword="null"/> to sort
    /// by each item's canonical JSON representation.
    /// </param>
    /// <returns>This settings instance, for chaining. Rules run in registration order.</returns>
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
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings CanonicalizeJson()
    {
        CanonicalizeObjectProperties = true;
        return this;
    }

    /// <summary>Replaces every JSON string containing a GUID with <c>{Guid}</c>.</summary>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings ScrubGuids()
    {
        ScrubGuidValues = true;
        return this;
    }

    /// <summary>Replaces every round-trip JSON date/time string with <c>{DateTime}</c>.</summary>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings ScrubDateTimes()
    {
        ScrubDateTimeValues = true;
        return this;
    }

    /// <summary>Sets automatic snapshot-update behavior and returns this instance.</summary>
    /// <param name="updateMode">The update policy; undefined enum values are retained but unsupported during matching.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings Updating(SnapshotUpdateMode updateMode)
    {
        UpdateMode = updateMode;
        return this;
    }

    /// <summary>Explicitly permits automatic snapshot updates in continuous integration.</summary>
    /// <returns>This settings instance, for chaining. The update mode remains unchanged.</returns>
    public SnapshotSettings AllowingUpdatesInContinuousIntegration()
    {
        AllowUpdatesInContinuousIntegration = true;
        return this;
    }

    /// <summary>Records matched snapshot paths in an instance-scoped catalog.</summary>
    /// <param name="catalog">The non-null caller-owned, thread-safe catalog to notify.</param>
    /// <returns>This settings instance, for chaining.</returns>
    public SnapshotSettings TrackingWith(SnapshotCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
        return this;
    }

    /// <summary>Disables diff-tool launching and returns this instance.</summary>
    /// <returns>This settings instance, for chaining, with any explicit tool cleared.</returns>
    public SnapshotSettings WithoutDiffTool()
    {
        LaunchDiffTool = false;
        DiffTool = null;
        return this;
    }

    /// <summary>Selects an explicit diff tool and returns this instance.</summary>
    /// <param name="diffTool">The non-null caller-owned configuration retained by these settings.</param>
    /// <returns>This settings instance, for chaining, with launching enabled outside CI.</returns>
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
            _directory = _directory,
            _directoryResolver = _directoryResolver,
            _snapshotName = _snapshotName,
            _variant = _variant,
            _jsonSerializerOptions = new JsonSerializerOptions(_jsonSerializerOptions),
            _updateMode = _updateMode,
            _launchDiffTool = _launchDiffTool,
            _diffTool = _diffTool,
            _allowUpdatesInContinuousIntegration = _allowUpdatesInContinuousIntegration,
            _catalog = _catalog,
            _overrides = _overrides,
            _includesGlobalDefaults = _includesGlobalDefaults,
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

    internal SnapshotSettings Merge(SnapshotSettings local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var merged = Copy();

        if (local._overrides.HasFlag(SettingOverrides.Directory))
        {
            merged._directory = local._directory;
        }
        if (local._overrides.HasFlag(SettingOverrides.DirectoryResolver))
        {
            merged._directoryResolver = local._directoryResolver;
        }
        if (local._overrides.HasFlag(SettingOverrides.SnapshotName))
        {
            merged._snapshotName = local._snapshotName;
        }
        if (local._overrides.HasFlag(SettingOverrides.Variant))
        {
            merged._variant = local._variant;
        }
        if (local._overrides.HasFlag(SettingOverrides.JsonSerializerOptions))
        {
            merged._jsonSerializerOptions = new JsonSerializerOptions(local._jsonSerializerOptions);
        }
        if (local._overrides.HasFlag(SettingOverrides.UpdateMode))
        {
            merged._updateMode = local._updateMode;
        }
        if (local._overrides.HasFlag(SettingOverrides.LaunchDiffTool))
        {
            merged._launchDiffTool = local._launchDiffTool;
        }
        if (local._overrides.HasFlag(SettingOverrides.DiffTool))
        {
            merged._diffTool = local._diffTool;
        }
        if (local._overrides.HasFlag(SettingOverrides.AllowUpdatesInContinuousIntegration))
        {
            merged._allowUpdatesInContinuousIntegration = local._allowUpdatesInContinuousIntegration;
        }
        if (local._overrides.HasFlag(SettingOverrides.Catalog))
        {
            merged._catalog = local._catalog;
        }

        merged._overrides |= local._overrides;
        foreach (var scrubber in local.Scrubbers)
        {
            merged.Scrubbers.Add(scrubber);
        }
        merged._scrubbedMembers.UnionWith(local._scrubbedMembers);
        merged._ignoredMembers.UnionWith(local._ignoredMembers);
        merged._pathRules.AddRange(local._pathRules);
        merged.ScrubGuidValues |= local.ScrubGuidValues;
        merged.ScrubDateTimeValues |= local.ScrubDateTimeValues;
        merged.CanonicalizeObjectProperties |= local.CanonicalizeObjectProperties;
        return merged;
    }

    internal bool IncludesGlobalDefaults => _includesGlobalDefaults;

    internal void MarkIncludesGlobalDefaults() => _includesGlobalDefaults = true;

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
        var value = Environment.GetEnvironmentVariable(UpdateModeEnvironmentVariable)?
            .Trim()
            .ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return SnapshotUpdateMode.None;
        }

        return UpdateModes.TryGetValue(value, out var mode)
            ? mode
            : throw new InvalidOperationException(
                $"Environment variable {UpdateModeEnvironmentVariable} must be 'none', 'missing', or 'all'.");
    }

    [Flags]
    private enum SettingOverrides
    {
        None = 0,
        Directory = 1 << 0,
        DirectoryResolver = 1 << 1,
        SnapshotName = 1 << 2,
        Variant = 1 << 3,
        JsonSerializerOptions = 1 << 4,
        UpdateMode = 1 << 5,
        LaunchDiffTool = 1 << 6,
        DiffTool = 1 << 7,
        AllowUpdatesInContinuousIntegration = 1 << 8,
        Catalog = 1 << 9
    }
}
