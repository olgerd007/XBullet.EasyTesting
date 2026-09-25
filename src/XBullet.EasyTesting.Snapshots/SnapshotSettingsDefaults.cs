namespace XBullet.EasyTesting.Snapshots;

/// <summary>
/// Stores reusable snapshot settings as a template. Each call creates an independent settings
/// object, avoiding cross-test interference even when the template is configured globally.
/// </summary>
public sealed class SnapshotSettingsDefaults
{
    private static SnapshotSettingsDefaults? _global;
    private readonly SnapshotSettings _template;

    /// <summary>
    /// Gets or sets the optional defaults used as the base for snapshot assertions. Configure this
    /// once while the test assembly is initialized. Each assertion receives an independent copy;
    /// explicit settings are merged into it and locally configured scalar values take precedence.
    /// </summary>
    public static SnapshotSettingsDefaults? Global
    {
        get => Volatile.Read(ref _global);
        set => Volatile.Write(ref _global, value);
    }

    /// <summary>Creates reusable defaults from a one-time configuration callback.</summary>
    public SnapshotSettingsDefaults(Action<SnapshotSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _template = new SnapshotSettings();
        configure(_template);
    }

    /// <summary>Creates an independent settings object and optionally applies local overrides.</summary>
    public SnapshotSettings Create(Action<SnapshotSettings>? configure = null)
    {
        var settings = _template.Copy();
        configure?.Invoke(settings);
        if (ReferenceEquals(this, Global))
        {
            settings.MarkIncludesGlobalDefaults();
        }
        return settings;
    }

    /// <summary>
    /// Creates settings from the global template, or package defaults when no global template is
    /// configured, and then applies per-assertion configuration.
    /// </summary>
    public static SnapshotSettings ExtendGlobal(Action<SnapshotSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var settings = CreateGlobalOrDefault();
        configure(settings);
        return settings;
    }

    internal static SnapshotSettings CreateGlobalOrDefault()
    {
        var global = Global;
        if (global is null)
        {
            return new SnapshotSettings();
        }

        var settings = global.Create();
        settings.MarkIncludesGlobalDefaults();
        return settings;
    }

    internal static SnapshotSettings MergeGlobalOrDefault(SnapshotSettings? local)
    {
        if (local?.IncludesGlobalDefaults == true)
        {
            return local;
        }

        var global = Global;
        if (global is null)
        {
            return local ?? new SnapshotSettings();
        }

        var settings = global.Create();
        settings.MarkIncludesGlobalDefaults();
        return local is null ? settings : settings.Merge(local);
    }
}
