using System.Runtime.CompilerServices;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Built-in snapshot assertions for controller responses.</summary>
public static class HttpResponseSnapshotExtensions
{
    /// <summary>
    /// Asserts that JSON HTTP content matches its committed <c>.verified.json</c> snapshot.
    /// The content is parsed and normalized with <c>System.Text.Json</c> before comparison.
    /// </summary>
    public static async Task ShouldMatchJsonSnapshot(
        this HttpContent content,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(content);

        var stream = await content.ReadAsStreamAsync(cancellationToken);
        if (!stream.CanSeek)
        {
            await SnapshotAssert.MatchJsonAsync(
                stream,
                snapshotSettings,
                cancellationToken,
                sourceFile,
                testName);
            return;
        }

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            await SnapshotAssert.MatchJsonAsync(
                stream,
                snapshotSettings,
                cancellationToken,
                sourceFile,
                testName);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Asserts that the JSON body of an HTTP response matches its committed
    /// <c>.verified.json</c> snapshot.
    /// </summary>
    public static Task ShouldMatchJsonBodySnapshot(
        this HttpResponseMessage response,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Content.ShouldMatchJsonSnapshot(
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }

    /// <summary>Asserts that a controller response matches its committed JSON snapshot.</summary>
    public static async Task ShouldMatchControllerSnapshot(
        this HttpResponseMessage response,
        ControllerSnapshotOptions? controllerOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            controllerOptions,
            cancellationToken);

        await SnapshotAssert.MatchAsync(
            snapshot,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }
}
