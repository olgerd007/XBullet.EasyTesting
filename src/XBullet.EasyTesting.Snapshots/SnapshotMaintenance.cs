namespace XBullet.EasyTesting.Snapshots;

/// <summary>Provides explicit preview-and-confirm operations for snapshot maintenance.</summary>
public static class SnapshotMaintenance
{
    /// <summary>Finds received snapshots below a directory without changing any files.</summary>
    public static IReadOnlyList<string> FindReceivedSnapshots(string directory) =>
        FindSnapshots(directory, "*.received.json");

    /// <summary>
    /// Accepts every received snapshot below a directory. Set <paramref name="confirmed"/> to
    /// <see langword="true"/> only after reviewing <see cref="FindReceivedSnapshots"/>.
    /// </summary>
    public static IReadOnlyList<string> AcceptReceivedSnapshots(
        string directory,
        bool confirmed = false)
    {
        EnsureConfirmed(confirmed);
        return FindReceivedSnapshots(directory)
            .Select(SnapshotAssert.AcceptReceived)
            .ToArray();
    }

    /// <summary>
    /// Removes the supplied verified snapshots. Every path is validated before any file is
    /// removed, and <paramref name="confirmed"/> must be <see langword="true"/>.
    /// </summary>
    public static IReadOnlyList<string> RemoveVerifiedSnapshots(
        IEnumerable<string> verifiedPaths,
        bool confirmed = false)
    {
        ArgumentNullException.ThrowIfNull(verifiedPaths);
        EnsureConfirmed(confirmed);

        var paths = verifiedPaths
            .Select(path =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(path);
                var fullPath = Path.GetFullPath(path);
                if (!fullPath.EndsWith(".verified.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Snapshot path '{fullPath}' must end with '.verified.json'.",
                        nameof(verifiedPaths));
                }

                return fullPath;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var removed = new List<string>(paths.Length);
        foreach (var path in paths)
        {
            using var pathLock = SnapshotPathLock.Acquire(path);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(path);
            }
        }

        return removed;
    }

    private static IReadOnlyList<string> FindSnapshots(string directory, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(fullDirectory, pattern, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureConfirmed(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "Snapshot maintenance changes require confirmed: true after previewing the affected files.");
        }
    }
}
