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
    /// <param name="response">
    /// The non-null response whose request metadata, status, headers, and content are read. The
    /// method does not own or dispose it; the caller remains responsible for disposal.
    /// </param>
    /// <param name="options">
    /// Controller snapshot inclusion, redaction, and scrubbing settings. When
    /// <see langword="null"/>, configured controller-snapshot defaults are used, or package
    /// defaults when no global template exists. Supplied options remain caller-owned.
    /// </param>
    /// <param name="settings">
    /// Verify naming, scrubbing, and comparison settings, or <see langword="null"/> to use
    /// Verify.Xunit defaults. The extension passes the instance to Verify without taking ownership.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels response-content reading and snapshot-model creation. It does not independently
    /// cancel Verify after the model has been created. The default token does not request
    /// cancellation.
    /// </param>
    /// <param name="sourceFile">
    /// The calling source-file path passed to Verify for snapshot naming and placement. The compiler
    /// supplies it by default; callers should normally omit it.
    /// </param>
    /// <returns>
    /// A Verify settings task for the asynchronous snapshot model. Configure it further if needed
    /// and await it to complete verification and any received-file side effects.
    /// </returns>
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
    /// <param name="response">
    /// The non-null response used to locate a recorder-attached exchange or construct one from its
    /// request and response content. The method does not own or dispose it.
    /// </param>
    /// <param name="options">
    /// Complete-exchange inclusion, redaction, and scrubbing settings. When
    /// <see langword="null"/>, recorder settings are used for a recorded response; otherwise,
    /// configured exchange defaults or package defaults apply. Options supplied for a recorded
    /// response must be compatible with the recorder's original options.
    /// </param>
    /// <param name="settings">
    /// Verify naming, scrubbing, and comparison settings, or <see langword="null"/> to use
    /// Verify.Xunit defaults. The extension passes the instance to Verify without taking ownership.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels exchange lookup and request or response content reading while the snapshot model is
    /// created. It does not independently cancel Verify afterward. The default token does not
    /// request cancellation.
    /// </param>
    /// <param name="sourceFile">
    /// The calling source-file path passed to Verify for snapshot naming and placement. The compiler
    /// supplies it by default; callers should normally omit it.
    /// </param>
    /// <returns>
    /// A Verify settings task for the asynchronous exchange model. Configure it further if needed
    /// and await it to complete verification and any received-file side effects.
    /// </returns>
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
    /// <param name="recorder">
    /// The non-null recorder whose completed exchanges are copied in request order. The method does
    /// not reset, own, or dispose it; exchanges captured after the copy are not included.
    /// </param>
    /// <param name="settings">
    /// Verify naming, scrubbing, and comparison settings, or <see langword="null"/> to use
    /// Verify.Xunit defaults. The extension passes the instance to Verify without taking ownership.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels snapshot-array creation before the recorder is copied. Snapshot creation is otherwise
    /// synchronous, and the token does not independently cancel Verify afterward. The default token
    /// does not request cancellation.
    /// </param>
    /// <param name="sourceFile">
    /// The calling source-file path passed to Verify for snapshot naming and placement. The compiler
    /// supplies it by default; callers should normally omit it.
    /// </param>
    /// <returns>
    /// A Verify settings task for the asynchronous exchange array. Configure it further if needed
    /// and await it to complete verification and any received-file side effects.
    /// </returns>
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
