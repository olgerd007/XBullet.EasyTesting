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

    /// <summary>
    /// Verifies the complete request and response represented by an HTTP response.
    /// JSON bodies are compared structurally rather than as formatting-sensitive strings.
    /// </summary>
    public static SettingsTask VerifyHttpExchangeSnapshot(
        this HttpResponseMessage response,
        HttpExchangeSnapshotOptions? options = null,
        VerifySettings? settings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(response);

        var snapshot = CreateVerifyExchangeSnapshotAsync(
            response,
            options,
            cancellationToken);

        return Verifier.Verify(snapshot, settings, sourceFile);
    }

    /// <summary>
    /// Verifies every complete request/response exchange captured by an HTTP exchange recorder.
    /// </summary>
    public static SettingsTask VerifyHttpExchangesSnapshot(
        this HttpExchangeRecorder recorder,
        VerifySettings? settings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(recorder);

        var snapshots = CreateVerifyExchangeSnapshotsAsync(recorder, cancellationToken);
        return Verifier.Verify(snapshots, settings, sourceFile);
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

    private static async Task<HttpExchangeSnapshot> CreateVerifyExchangeSnapshotAsync(
        HttpResponseMessage response,
        HttpExchangeSnapshotOptions? options,
        CancellationToken cancellationToken)
    {
        var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            options,
            cancellationToken);
        return ToVerifyExchangeSnapshot(snapshot);
    }

    private static async Task<IReadOnlyList<HttpExchangeSnapshot>> CreateVerifyExchangeSnapshotsAsync(
        HttpExchangeRecorder recorder,
        CancellationToken cancellationToken)
    {
        var snapshots = await recorder.CreateSnapshotsAsync(cancellationToken);
        return snapshots.Select(ToVerifyExchangeSnapshot).ToArray();
    }

    internal static HttpExchangeSnapshot ToVerifyExchangeSnapshot(HttpExchangeSnapshot snapshot)
    {
        var request = snapshot.Request?.Body is JsonElement requestJson
            ? snapshot.Request with { Body = ToVerifyValue(requestJson) }
            : snapshot.Request;
        var response = snapshot.Response?.Body is JsonElement responseJson
            ? snapshot.Response with { Body = ToVerifyValue(responseJson) }
            : snapshot.Response;

        return snapshot with { Request = request, Response = response };
    }

    internal static object? ToVerifyValue(JsonElement element) =>
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
