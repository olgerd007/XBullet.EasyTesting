namespace XBullet.EasyTesting.Snapshots;

/// <summary>Describes an external application used to compare verified and received snapshots.</summary>
public sealed class SnapshotDiffTool
{
    /// <summary>Creates a diff-tool configuration.</summary>
    public SnapshotDiffTool(string executable, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        Executable = executable;
        Arguments = arguments;
    }

    /// <summary>Gets the executable path or command name.</summary>
    public string Executable { get; }

    /// <summary>
    /// Gets command arguments. Use <c>{verified}</c> and <c>{received}</c> as path placeholders.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Creates a Visual Studio diff configuration.</summary>
    public static SnapshotDiffTool VisualStudio(string executable = "devenv.exe") =>
        new(executable, "/diff", "{verified}", "{received}");

    /// <summary>Creates a Visual Studio Code diff configuration.</summary>
    public static SnapshotDiffTool VisualStudioCode(string executable = "code") =>
        new(executable, "--diff", "{verified}", "{received}");

    /// <summary>Creates a JetBrains Rider diff configuration.</summary>
    public static SnapshotDiffTool Rider(string executable = "rider") =>
        new(executable, "diff", "{verified}", "{received}");
}
