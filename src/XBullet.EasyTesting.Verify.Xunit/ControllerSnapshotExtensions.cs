using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Snapshots;
using VerifyTests;
using VerifyXunit;

namespace XBullet.EasyTesting.Verify.Xunit;

/// <summary>Verify.Xunit extensions for controller responses.</summary>
public static class ControllerSnapshotExtensions
{
    /// <summary>
    /// Verifies the request, status, stable headers, and normalized body of a controller response.
    /// JSON bodies are compared structurally rather than as formatting-sensitive strings.
    /// </summary>
    public static SettingsTask VerifyControllerSnapshot(
        this HttpResponseMessage response,
        ControllerSnapshotOptions? options = null,
        VerifySettings? settings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(response);

        var snapshot = ControllerResponseSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        return Verifier.Verify(snapshot, settings, sourceFile);
    }
}
