namespace XBullet.EasyTesting.Snapshots;

/// <summary>
/// Stores reusable controller snapshot options as a template. Each controller snapshot receives an
/// independent copy, and explicitly supplied options are merged into the global template.
/// </summary>
public sealed class ControllerSnapshotOptionsDefaults
{
    private static ControllerSnapshotOptionsDefaults? _global;
    private readonly ControllerSnapshotOptions _template;

    /// <summary>
    /// Gets or sets the optional defaults used as the base for controller snapshots. Configure this
    /// once while the test assembly is initialized.
    /// </summary>
    public static ControllerSnapshotOptionsDefaults? Global
    {
        get => Volatile.Read(ref _global);
        set => Volatile.Write(ref _global, value);
    }

    /// <summary>Creates reusable defaults from a one-time configuration callback.</summary>
    public ControllerSnapshotOptionsDefaults(Action<ControllerSnapshotOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _template = new ControllerSnapshotOptions();
        configure(_template);
    }

    /// <summary>Creates an independent options object and optionally applies local overrides.</summary>
    public ControllerSnapshotOptions Create(Action<ControllerSnapshotOptions>? configure = null)
    {
        var options = _template.Copy();
        configure?.Invoke(options);
        if (ReferenceEquals(this, Global))
        {
            options.MarkIncludesGlobalDefaults();
        }
        return options;
    }

    /// <summary>
    /// Creates options from the global template, or package defaults when no global template is
    /// configured, and then applies per-assertion configuration.
    /// </summary>
    public static ControllerSnapshotOptions ExtendGlobal(Action<ControllerSnapshotOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = MergeGlobalOrDefault(local: null);
        configure(options);
        return options;
    }

    internal static ControllerSnapshotOptions MergeGlobalOrDefault(ControllerSnapshotOptions? local)
    {
        if (local?.IncludesGlobalDefaults == true)
        {
            return local;
        }

        var global = Global;
        if (global is null)
        {
            return local ?? new ControllerSnapshotOptions();
        }

        var options = global.Create();
        options.MarkIncludesGlobalDefaults();
        return local is null ? options : options.Merge(local);
    }
}
