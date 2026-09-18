using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Framework-independent file snapshot assertions.</summary>
public static class SnapshotAssert
{
    /// <summary>
    /// Serializes a value and compares it with its committed <c>.verified.json</c> snapshot.
    /// A <c>.received.json</c> file is written when the snapshot is new or differs.
    /// </summary>
    public static async Task MatchAsync(
        object? actual,
        SnapshotSettings? settings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        settings ??= new SnapshotSettings();

        var serialized = JsonSerializer.Serialize(actual, settings.JsonSerializerOptions);
        await MatchSerializedAsync(
            serialized,
            settings,
            cancellationToken,
            sourceFile,
            testName);
    }

    /// <summary>
    /// Parses JSON content with <see cref="JsonDocument"/> and compares its normalized JSON
    /// representation with a committed <c>.verified.json</c> snapshot.
    /// </summary>
    public static async Task MatchJsonAsync(
        string actualJson,
        SnapshotSettings? settings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(actualJson);
        settings ??= new SnapshotSettings();

        var json = JsonSnapshotContent.Parse(actualJson, settings.JsonSerializerOptions);
        var serialized = JsonSerializer.Serialize(
            json,
            settings.JsonSerializerOptions);

        await MatchSerializedAsync(
            serialized,
            settings,
            cancellationToken,
            sourceFile,
            testName);
    }

    internal static async Task MatchJsonAsync(
        Stream actualJson,
        SnapshotSettings? settings,
        CancellationToken cancellationToken,
        string sourceFile,
        string testName)
    {
        ArgumentNullException.ThrowIfNull(actualJson);
        settings ??= new SnapshotSettings();

        var json = await JsonSnapshotContent.ParseAsync(
            actualJson,
            settings.JsonSerializerOptions,
            cancellationToken);
        var serialized = JsonSerializer.Serialize(
            json,
            settings.JsonSerializerOptions);

        await MatchSerializedAsync(
            serialized,
            settings,
            cancellationToken,
            sourceFile,
            testName);
    }

    private static async Task MatchSerializedAsync(
        string serialized,
        SnapshotSettings settings,
        CancellationToken cancellationToken,
        string sourceFile,
        string testName)
    {
        serialized = StructuredSnapshotScrubber.Apply(serialized, settings);
        foreach (var scrubber in settings.Scrubbers)
        {
            serialized = scrubber(serialized);
        }

        if (settings.Scrubbers.Count > 0)
        {
            try
            {
                _ = JsonSnapshotContent.Parse(serialized, settings.JsonSerializerOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    "A custom snapshot scrubber produced invalid JSON.",
                    exception);
            }
        }

        serialized = NormalizeNewLines(serialized);
        var paths = ResolvePaths(settings, sourceFile, testName);
        settings.Catalog?.Record(paths.Verified);
        using var pathLock = await SnapshotPathLock.AcquireAsync(paths.Verified, cancellationToken);
        System.IO.Directory.CreateDirectory(paths.Directory);
        EnsureNoPortablePathCollision(paths);

        if (!File.Exists(paths.Verified))
        {
            if (settings.UpdateMode is SnapshotUpdateMode.Missing or SnapshotUpdateMode.All)
            {
                EnsureAutomaticUpdateIsAllowed(settings);
                await WriteSnapshotAsync(paths.Verified, serialized, cancellationToken);
                DeleteIfExists(paths.Received);
                return;
            }

            await WriteSnapshotAsync(paths.Received, serialized, cancellationToken);
            var difference = SnapshotJsonDifference.MissingExpected(serialized);
            throw new SnapshotMismatchException(
                $"Snapshot is not approved. Review '{paths.Received}' and rename it to '{paths.Verified}'.",
                paths.Verified,
                paths.Received,
                differencePath: difference.Path,
                expectedValue: difference.Expected,
                actualValue: difference.Actual);
        }

        var expected = NormalizeNewLines(await File.ReadAllTextAsync(paths.Verified, cancellationToken));
        if (!string.Equals(expected, serialized, StringComparison.Ordinal))
        {
            if (settings.UpdateMode == SnapshotUpdateMode.All)
            {
                EnsureAutomaticUpdateIsAllowed(settings);
                await WriteSnapshotAsync(paths.Verified, serialized, cancellationToken);
                DeleteIfExists(paths.Received);
                return;
            }

            await WriteSnapshotAsync(paths.Received, serialized, cancellationToken);
            var location = FindFirstDifference(expected, serialized);
            var difference = SnapshotJsonDifference.Find(expected, serialized);
            var diffToolLaunched = SnapshotDiffLauncher.TryLaunch(
                settings,
                paths.Verified,
                paths.Received);
            throw new SnapshotMismatchException(
                $"Snapshot differs at {difference.Path}: expected {difference.Expected}; " +
                $"actual {difference.Actual}. Text location: line {location.Line}, " +
                $"column {location.Column}. " +
                $"Expected '{paths.Verified}'; received '{paths.Received}'.",
                paths.Verified,
                paths.Received,
                diffToolLaunched,
                difference.Path,
                difference.Expected,
                difference.Actual);
        }

        DeleteIfExists(paths.Received);
    }

    /// <summary>Promotes one <c>.received.json</c> file to its <c>.verified.json</c> counterpart.</summary>
    public static string AcceptReceived(string receivedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedPath);
        var fullReceivedPath = Path.GetFullPath(receivedPath);
        const string receivedSuffix = ".received.json";
        if (!fullReceivedPath.EndsWith(receivedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The snapshot path must end with '{receivedSuffix}'.",
                nameof(receivedPath));
        }

        var verifiedPath = string.Concat(
            fullReceivedPath.AsSpan(0, fullReceivedPath.Length - receivedSuffix.Length),
            ".verified.json");
        using var pathLock = SnapshotPathLock.Acquire(verifiedPath);
        if (!File.Exists(fullReceivedPath))
        {
            throw new FileNotFoundException("The received snapshot does not exist.", fullReceivedPath);
        }

        File.Move(fullReceivedPath, verifiedPath, overwrite: true);
        return verifiedPath;
    }

    private static async Task WriteSnapshotAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The snapshot directory could not be determined.", nameof(path));
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private static SnapshotPaths ResolvePaths(
        SnapshotSettings settings,
        string sourceFile,
        string testName)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            throw new ArgumentException("The calling source file could not be determined.", nameof(sourceFile));
        }

        var sourceDirectory = Path.GetFullPath(
            Path.GetDirectoryName(sourceFile)
                ?? throw new ArgumentException("The calling source directory could not be determined.", nameof(sourceFile)));
        var directory = string.IsNullOrWhiteSpace(settings.Directory)
            ? Path.Combine(sourceDirectory, "__snapshots__")
            : Path.GetFullPath(settings.Directory, sourceDirectory);
        var requestedName = settings.SnapshotName ?? testName;
        var safeName = SanitizeFileNameComponent(requestedName);
        var sourceName = SanitizeFileNameComponent(Path.GetFileNameWithoutExtension(sourceFile));
        var variant = settings.Variant is null
            ? null
            : SanitizeFileNameComponent(settings.Variant);
        var fileName = variant is null
            ? $"{sourceName}.{safeName}"
            : $"{sourceName}.{safeName}.{variant}";

        return new SnapshotPaths(
            directory,
            Path.Combine(directory, $"{fileName}.verified.json"),
            Path.Combine(directory, $"{fileName}.received.json"));
    }

    private static string SanitizeFileNameComponent(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A snapshot name is required.", nameof(name));
        }

        var normalized = name.Normalize(NormalizationForm.FormC);
        var trailingStart = normalized.Length;
        while (trailingStart > 0 && normalized[trailingStart - 1] is ' ' or '.')
        {
            trailingStart--;
        }

        var builder = new StringBuilder(normalized.Length);
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '%' ||
                character < ' ' ||
                character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ||
                index >= trailingStart)
            {
                builder.Append('%');
                builder.Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        var sanitized = builder.ToString();
        if (IsReservedWindowsDeviceName(sanitized))
        {
            sanitized = $"%{(int)sanitized[0]:X4}{sanitized[1..]}";
        }

        return sanitized;
    }

    private static bool IsReservedWindowsDeviceName(string name)
    {
        var stem = name.Split('.', 2)[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            IsNumberedDeviceName(stem, "COM") ||
            IsNumberedDeviceName(stem, "LPT");
    }

    private static bool IsNumberedDeviceName(string name, string prefix) =>
        name.Length == prefix.Length + 1 &&
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        name[^1] is >= '1' and <= '9';

    private static void EnsureNoPortablePathCollision(SnapshotPaths paths)
    {
        foreach (var targetPath in new[] { paths.Verified, paths.Received })
        {
            var targetName = Path.GetFileName(targetPath);
            foreach (var existingPath in System.IO.Directory.EnumerateFiles(paths.Directory))
            {
                var existingName = Path.GetFileName(existingPath);
                if (string.Equals(existingName, targetName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existingName, targetName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Snapshot path '{targetPath}' conflicts with existing file '{existingPath}' " +
                        "on case-insensitive file systems. Use a distinct snapshot name or variant.");
                }
            }
        }
    }

    private static string NormalizeNewLines(string value) =>
        value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void EnsureAutomaticUpdateIsAllowed(SnapshotSettings settings)
    {
        if (ContinuousIntegrationEnvironment.IsDetected() &&
            !settings.AllowUpdatesInContinuousIntegration)
        {
            throw new InvalidOperationException(
                "Automatic snapshot updates are disabled in continuous integration. " +
                $"Set {SnapshotSettings.AllowCiUpdatesEnvironmentVariable}=true or call " +
                $"{nameof(SnapshotSettings.AllowingUpdatesInContinuousIntegration)}() to opt in explicitly.");
        }
    }

    private static (int Line, int Column) FindFirstDifference(string expected, string actual)
    {
        var sharedLength = Math.Min(expected.Length, actual.Length);
        var index = 0;
        while (index < sharedLength && expected[index] == actual[index])
        {
            index++;
        }

        var line = 1;
        var column = 1;
        for (var characterIndex = 0; characterIndex < index; characterIndex++)
        {
            if (actual[characterIndex] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private sealed record SnapshotPaths(string Directory, string Verified, string Received);
}
