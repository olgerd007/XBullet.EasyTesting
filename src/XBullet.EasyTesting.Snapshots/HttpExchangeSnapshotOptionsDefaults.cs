namespace XBullet.EasyTesting.Snapshots;

/// <summary>
/// Stores reusable HTTP exchange snapshot options as a template. Each exchange snapshot receives
/// an independent copy, and explicitly supplied options are merged into the global template.
/// </summary>
public sealed class HttpExchangeSnapshotOptionsDefaults
{
    private static HttpExchangeSnapshotOptionsDefaults? _global;
    private readonly HttpExchangeSnapshotOptions _template;

    /// <summary>
    /// Gets or sets the optional defaults used as the base for HTTP exchange snapshots. Configure
    /// this once while the test assembly is initialized.
    /// </summary>
    /// <value>The process-wide defaults template, or <see langword="null"/> to use package defaults. Reads and writes are atomic.</value>
    public static HttpExchangeSnapshotOptionsDefaults? Global
    {
        get => Volatile.Read(ref _global);
        set => Volatile.Write(ref _global, value);
    }

    /// <summary>Creates reusable defaults from a one-time configuration callback.</summary>
    /// <param name="configure">Non-null callback invoked once immediately with a new mutable template. The callback is not retained.</param>
    public HttpExchangeSnapshotOptionsDefaults(Action<HttpExchangeSnapshotOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _template = new HttpExchangeSnapshotOptions();
        configure(_template);
    }

    /// <summary>Creates an independent options object and optionally applies local overrides.</summary>
    /// <param name="configure">Optional callback invoked once with the copied mutable options. <see langword="null"/> applies no overrides.</param>
    /// <returns>A new caller-owned mutable options object; changing it does not change this template.</returns>
    public HttpExchangeSnapshotOptions Create(Action<HttpExchangeSnapshotOptions>? configure = null)
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
    /// <param name="configure">Non-null callback invoked once with the new mutable options after global or package defaults are applied.</param>
    /// <returns>A new caller-owned mutable options object marked as already containing global defaults.</returns>
    public static HttpExchangeSnapshotOptions ExtendGlobal(
        Action<HttpExchangeSnapshotOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = MergeGlobalOrDefault(local: null);
        configure(options);
        return options;
    }

    internal static HttpExchangeSnapshotOptions MergeGlobalOrDefault(
        HttpExchangeSnapshotOptions? local)
    {
        if (local?.IncludesGlobalDefaults == true)
        {
            return local;
        }

        var global = Global;
        if (global is null)
        {
            return local ?? new HttpExchangeSnapshotOptions();
        }

        var options = global.Create();
        options.MarkIncludesGlobalDefaults();
        return local is null ? options : options.Merge(local);
    }
}
