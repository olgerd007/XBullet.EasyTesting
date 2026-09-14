namespace XBullet.EasyTesting.Snapshots;

/// <summary>Thrown when a received snapshot has no approved file or differs from it.</summary>
public sealed class SnapshotMismatchException : Exception
{
    internal SnapshotMismatchException(
        string message,
        string verifiedPath,
        string receivedPath,
        bool diffToolLaunched = false)
        : base(message)
    {
        VerifiedPath = verifiedPath;
        ReceivedPath = receivedPath;
        DiffToolLaunched = diffToolLaunched;
    }

    /// <summary>Gets the path of the expected, committed snapshot.</summary>
    public string VerifiedPath { get; }

    /// <summary>Gets the path containing the newly received snapshot.</summary>
    public string ReceivedPath { get; }

    /// <summary>Gets whether a diff viewer was started for this mismatch.</summary>
    public bool DiffToolLaunched { get; }
}
