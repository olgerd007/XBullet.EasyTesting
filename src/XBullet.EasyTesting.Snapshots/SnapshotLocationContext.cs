namespace XBullet.EasyTesting.Snapshots;

/// <summary>Describes the calling test when resolving a snapshot directory.</summary>
public sealed class SnapshotLocationContext
{
    internal SnapshotLocationContext(
        string sourceFile,
        string sourceDirectory,
        string sourceFileName,
        string testName,
        string snapshotName,
        string? variant)
    {
        SourceFile = sourceFile;
        SourceDirectory = sourceDirectory;
        SourceFileName = sourceFileName;
        TestName = testName;
        SnapshotName = snapshotName;
        Variant = variant;
    }

    /// <summary>Gets the full caller source-file path embedded by the compiler.</summary>
    public string SourceFile { get; }

    /// <summary>Gets the full caller source directory.</summary>
    public string SourceDirectory { get; }

    /// <summary>Gets the caller source filename, including its extension.</summary>
    public string SourceFileName { get; }

    /// <summary>Gets the caller member name.</summary>
    public string TestName { get; }

    /// <summary>Gets the configured snapshot name or caller member name.</summary>
    public string SnapshotName { get; }

    /// <summary>Gets the optional snapshot variant.</summary>
    public string? Variant { get; }
}
