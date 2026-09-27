using System.Collections.Concurrent;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>
/// Tracks snapshots observed during an explicit test run and identifies verified files that were
/// not exercised. Keep one catalog for the test scope whose snapshots should be audited.
/// </summary>
public sealed class SnapshotCatalog
{
    private readonly ConcurrentDictionary<string, byte> _observedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets a stable copy of the verified snapshot paths observed by this catalog.</summary>
    /// <value>A newly allocated, case-insensitively sorted list of full paths. The list may be empty and can be read independently of later catalog updates.</value>
    public IReadOnlyList<string> ObservedVerifiedSnapshots =>
        _observedPaths.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Finds verified snapshot files below a directory that have not been observed by this
    /// catalog. Call this only after all snapshots in the tracked test scope have executed.
    /// </summary>
    /// <param name="directory">Non-empty absolute or current-directory-relative root to search recursively. A missing directory is accepted.</param>
    /// <returns>Full paths of unobserved verified snapshots, sorted case-insensitively. The operation does not modify files or catalog state.</returns>
    public IReadOnlyList<string> FindObsoleteSnapshots(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories)
            .Where(path => SnapshotMaintenance.IsSnapshotFile(path, ".verified."))
            .Select(Path.GetFullPath)
            .Where(path => !_observedPaths.ContainsKey(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal void Record(string verifiedPath) =>
        _observedPaths.TryAdd(Path.GetFullPath(verifiedPath), 0);
}
