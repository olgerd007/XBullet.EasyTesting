using System.Runtime.CompilerServices;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Built-in snapshot assertions for controller responses.</summary>
public static class HttpResponseSnapshotExtensions
{
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
