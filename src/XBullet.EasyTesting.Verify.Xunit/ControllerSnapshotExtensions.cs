using System.Runtime.CompilerServices;
using System.Text.Json;
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

        var snapshot = CreateVerifySnapshotAsync(
            response,
            options,
            cancellationToken);

        return Verifier.Verify(snapshot, settings, sourceFile);
    }

    private static async Task<ControllerResponseSnapshot> CreateVerifySnapshotAsync(
        HttpResponseMessage response,
        ControllerSnapshotOptions? options,
        CancellationToken cancellationToken)
    {
        var snapshot = await ControllerResponseSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);

        return snapshot.Body is JsonElement json
            ? snapshot with { Body = ToVerifyValue(json) }
            : snapshot;
    }

    private static object? ToVerifyValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ToVerifyValue(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ToVerifyValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalNumber) => decimalNumber,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
}
