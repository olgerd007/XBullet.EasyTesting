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
    /// Gets or sets the optional defaults used when a snapshot assertion does not receive explicit
    /// settings. Configure this once while the test assembly is initialized. Each assertion receives
    /// an independent copy of the template.
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
        return settings;
    }

    internal static SnapshotSettings CreateGlobalOrDefault()
    {
        var global = Global;
        return global is null ? new SnapshotSettings() : global.Create();
    }
}
