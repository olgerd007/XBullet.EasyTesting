namespace XBullet.EasyTesting.Snapshots;

/// <summary>Describes an external application used to compare verified and received snapshots.</summary>
public sealed class SnapshotDiffTool
{
    /// <summary>Creates a diff-tool configuration.</summary>
    /// <param name="executable">Non-empty executable path or command name. Resolution is delegated to the operating system when the tool is launched.</param>
    /// <param name="arguments">
    /// Non-null argument array. Entries may contain <c>{verified}</c> and <c>{received}</c> placeholders;
    /// other values are passed unchanged. The array is retained and exposed, so callers must not mutate it after construction.
    /// </param>
    public SnapshotDiffTool(string executable, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        Executable = executable;
        Arguments = arguments;
    }

    /// <summary>Gets the executable path or command name.</summary>
    /// <value>The non-empty value supplied at construction.</value>
    public string Executable { get; }

    /// <summary>
    /// Gets command arguments. Use <c>{verified}</c> and <c>{received}</c> as path placeholders.
    /// </summary>
    /// <value>The retained caller-supplied argument array, exposed as a read-only list.</value>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Creates a Visual Studio diff configuration.</summary>
    /// <param name="executable">Non-empty executable path or command name. The default is <c>devenv.exe</c>.</param>
    /// <returns>A new configuration that invokes Visual Studio's <c>/diff</c> command with verified and received paths.</returns>
    public static SnapshotDiffTool VisualStudio(string executable = "devenv.exe") =>
        new(executable, "/diff", "{verified}", "{received}");

    /// <summary>Creates a Visual Studio Code diff configuration.</summary>
    /// <param name="executable">Non-empty executable path or command name. The default is <c>code</c>.</param>
    /// <returns>A new configuration that invokes Visual Studio Code's <c>--diff</c> command with verified and received paths.</returns>
    public static SnapshotDiffTool VisualStudioCode(string executable = "code") =>
        new(executable, "--diff", "{verified}", "{received}");

    /// <summary>Creates a JetBrains Rider diff configuration.</summary>
    /// <param name="executable">Non-empty executable path or command name. The default is <c>rider</c>.</param>
    /// <returns>A new configuration that invokes Rider's <c>diff</c> command with verified and received paths.</returns>
    public static SnapshotDiffTool Rider(string executable = "rider") =>
        new(executable, "diff", "{verified}", "{received}");
}
