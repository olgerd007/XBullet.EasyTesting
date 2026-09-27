namespace XBullet.EasyTesting.Snapshots;

/// <summary>Thrown when a received snapshot has no approved file or differs from it.</summary>
public sealed class SnapshotMismatchException : Exception
{
    internal SnapshotMismatchException(
        string message,
        string verifiedPath,
        string receivedPath,
        bool diffToolLaunched = false,
        string? differencePath = null,
        string? expectedValue = null,
        string? actualValue = null)
        : base(message)
    {
        VerifiedPath = verifiedPath;
        ReceivedPath = receivedPath;
        DiffToolLaunched = diffToolLaunched;
        DifferencePath = differencePath;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
    }

    /// <summary>Gets the path of the expected, committed snapshot.</summary>
    /// <value>The normalized verified-file path involved in the mismatch.</value>
    public string VerifiedPath { get; }

    /// <summary>Gets the path containing the newly received snapshot.</summary>
    /// <value>The runtime-qualified received-file path written for review.</value>
    public string ReceivedPath { get; }

    /// <summary>Gets whether a diff viewer was started for this mismatch.</summary>
    /// <value><see langword="true"/> only when a viewer process was successfully started.</value>
    public bool DiffToolLaunched { get; }

    /// <summary>Gets the JSONPath of the first structural difference, when available.</summary>
    /// <value>The first structural JSONPath or text location marker, or <see langword="null"/>.</value>
    public string? DifferencePath { get; }

    /// <summary>Gets a compact representation of the expected value at the difference.</summary>
    /// <value>The expected excerpt, or <see langword="null"/> when unavailable. It may contain sensitive snapshot data.</value>
    public string? ExpectedValue { get; }

    /// <summary>Gets a compact representation of the actual value at the difference.</summary>
    /// <value>The received excerpt, or <see langword="null"/> when unavailable. It may contain sensitive snapshot data.</value>
    public string? ActualValue { get; }
}
