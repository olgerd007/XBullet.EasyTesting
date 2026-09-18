namespace XBullet.EasyTesting.Snapshots;

/// <summary>Provides explicit preview-and-confirm operations for snapshot maintenance.</summary>
public static class SnapshotMaintenance
{
    /// <summary>Finds received snapshots below a directory without changing any files.</summary>
    public static IReadOnlyList<string> FindReceivedSnapshots(string directory) =>
        FindSnapshots(directory, ".received.");

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
                if (!IsSnapshotFile(fullPath, ".verified."))
                {
                    throw new ArgumentException(
                        $"Snapshot path '{fullPath}' must be a verified snapshot file.",
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

    private static IReadOnlyList<string> FindSnapshots(string directory, string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories)
            .Where(path => IsSnapshotFile(path, marker))
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsSnapshotFile(string path, string marker)
    {
        var fileName = Path.GetFileName(path);
        var markerIndex = fileName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var extensionIndex = fileName.LastIndexOf('.');
        return markerIndex > 0 &&
            extensionIndex >= markerIndex + marker.Length - 1 &&
            extensionIndex < fileName.Length - 1;
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
