namespace XBullet.EasyTesting.Snapshots;

/// <summary>
/// Stores reusable snapshot settings as an instance-scoped template. Each call creates an
/// independent settings object, avoiding mutable global defaults and cross-test interference.
/// </summary>
public sealed class SnapshotSettingsDefaults
{
    private readonly SnapshotSettings _template;

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
}
