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

        using var document = JsonDocument.Parse(
            actualJson,
            CreateDocumentOptions(settings.JsonSerializerOptions));
        var serialized = JsonSerializer.Serialize(
            document.RootElement,
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

        using var document = await JsonDocument.ParseAsync(
            actualJson,
            CreateDocumentOptions(settings.JsonSerializerOptions),
            cancellationToken);
        var serialized = JsonSerializer.Serialize(
            document.RootElement,
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

        serialized = NormalizeNewLines(serialized);
        var paths = ResolvePaths(settings, sourceFile, testName);
        System.IO.Directory.CreateDirectory(paths.Directory);

        if (!File.Exists(paths.Verified))
        {
            if (settings.UpdateMode is SnapshotUpdateMode.Missing or SnapshotUpdateMode.All)
            {
                await WriteSnapshotAsync(paths.Verified, serialized, cancellationToken);
                DeleteIfExists(paths.Received);
                return;
            }

            await WriteSnapshotAsync(paths.Received, serialized, cancellationToken);
            throw new SnapshotMismatchException(
                $"Snapshot is not approved. Review '{paths.Received}' and rename it to '{paths.Verified}'.",
                paths.Verified,
                paths.Received);
        }

        var expected = NormalizeNewLines(await File.ReadAllTextAsync(paths.Verified, cancellationToken));
        if (!string.Equals(expected, serialized, StringComparison.Ordinal))
        {
            if (settings.UpdateMode == SnapshotUpdateMode.All)
            {
                await WriteSnapshotAsync(paths.Verified, serialized, cancellationToken);
                DeleteIfExists(paths.Received);
                return;
            }

            await WriteSnapshotAsync(paths.Received, serialized, cancellationToken);
            var difference = FindFirstDifference(expected, serialized);
            var diffToolLaunched = SnapshotDiffLauncher.TryLaunch(
                settings,
                paths.Verified,
                paths.Received);
            throw new SnapshotMismatchException(
                $"Snapshot differs at line {difference.Line}, column {difference.Column}. " +
                $"Expected '{paths.Verified}'; received '{paths.Received}'.",
                paths.Verified,
                paths.Received,
                diffToolLaunched);
        }

        DeleteIfExists(paths.Received);
    }

    private static JsonDocumentOptions CreateDocumentOptions(JsonSerializerOptions options) => new()
    {
        AllowTrailingCommas = options.AllowTrailingCommas,
        CommentHandling = options.ReadCommentHandling,
        MaxDepth = options.MaxDepth
    };

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

        if (!File.Exists(fullReceivedPath))
        {
            throw new FileNotFoundException("The received snapshot does not exist.", fullReceivedPath);
        }

        var verifiedPath = string.Concat(
            fullReceivedPath.AsSpan(0, fullReceivedPath.Length - receivedSuffix.Length),
            ".verified.json");
        File.Move(fullReceivedPath, verifiedPath, overwrite: true);
        return verifiedPath;
    }

    private static async Task WriteSnapshotAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
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
        var safeName = SanitizeFileName(requestedName);
        var sourceName = Path.GetFileNameWithoutExtension(sourceFile);
        var fileName = $"{sourceName}.{safeName}";

        return new SnapshotPaths(
            directory,
            Path.Combine(directory, $"{fileName}.verified.json"),
            Path.Combine(directory, $"{fileName}.received.json"));
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A snapshot name is required.", nameof(name));
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character));
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
