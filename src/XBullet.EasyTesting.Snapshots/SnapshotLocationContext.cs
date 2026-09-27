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
    /// <value>The absolute or compiler-supplied path used as the snapshot location anchor.</value>
    public string SourceFile { get; }

    /// <summary>Gets the full caller source directory.</summary>
    /// <value>The normalized full directory containing <see cref="SourceFile"/>.</value>
    public string SourceDirectory { get; }

    /// <summary>Gets the caller source filename, including its extension.</summary>
    /// <value>The filename portion of <see cref="SourceFile"/>, including extension.</value>
    public string SourceFileName { get; }

    /// <summary>Gets the caller member name.</summary>
    /// <value>The compiler-supplied caller member name, which may be empty if supplied manually.</value>
    public string TestName { get; }

    /// <summary>Gets the configured snapshot name or caller member name.</summary>
    /// <value>The name requested before filename sanitization.</value>
    public string SnapshotName { get; }

    /// <summary>Gets the optional snapshot variant.</summary>
    /// <value>The requested variant before filename sanitization, or <see langword="null"/>.</value>
    public string? Variant { get; }
}
