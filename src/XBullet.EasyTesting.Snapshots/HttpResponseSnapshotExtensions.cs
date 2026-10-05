using System.Runtime.CompilerServices;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Built-in snapshot assertions for controller responses.</summary>
public static class HttpResponseSnapshotExtensions
{
    /// <summary>
    /// Asserts that JSON HTTP content matches its committed <c>.verified.json</c> snapshot.
    /// The content is parsed and normalized with <c>System.Text.Json</c> before comparison.
    /// </summary>
    /// <param name="content">The caller-owned, non-null HTTP content to read. It is not disposed; a seekable content stream has its position restored, while a non-seekable stream is consumed.</param>
    /// <param name="snapshotSettings">Optional snapshot settings. <see langword="null"/> uses the effective global or default settings.</param>
    /// <param name="cancellationToken">Token that cancels content reading and snapshot file operations. The default token does not cancel the operation.</param>
    /// <param name="sourceFile">Calling source-file path used to locate and name the snapshot. The compiler supplies this value by default.</param>
    /// <param name="testName">Calling member name used as the default snapshot name. The compiler supplies this value by default.</param>
    /// <returns>A task that completes when the normalized JSON matches or is updated; a mismatch writes a received file and throws.</returns>
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
    /// <param name="response">The caller-owned, non-null response whose content is read. Neither the response nor its content is disposed.</param>
    /// <param name="snapshotSettings">Optional snapshot settings. <see langword="null"/> uses the effective global or default settings.</param>
    /// <param name="cancellationToken">Token that cancels content reading and snapshot file operations. The default token does not cancel the operation.</param>
    /// <param name="sourceFile">Calling source-file path used to locate and name the snapshot. The compiler supplies this value by default.</param>
    /// <param name="testName">Calling member name used as the default snapshot name. The compiler supplies this value by default.</param>
    /// <returns>A task that completes when the normalized JSON body matches or is updated; a mismatch writes a received file and throws.</returns>
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
    /// <param name="response">The caller-owned, non-null response to capture. It is read but not disposed.</param>
    /// <param name="controllerOptions">Optional capture and redaction options. <see langword="null"/> uses the effective global or default controller options.</param>
    /// <param name="snapshotSettings">Optional snapshot settings. <see langword="null"/> uses the effective global or default settings.</param>
    /// <param name="cancellationToken">Token that cancels response reading and snapshot file operations. The default token does not cancel the operation.</param>
    /// <param name="sourceFile">Calling source-file path used to locate and name the snapshot. The compiler supplies this value by default.</param>
    /// <param name="testName">Calling member name used as the default snapshot name. The compiler supplies this value by default.</param>
    /// <returns>A task that completes when the captured response matches or is updated; a mismatch writes a received file and throws.</returns>
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

    /// <summary>
    /// Asserts that the complete request and response represented by an HTTP response match their
    /// committed snapshot. JSON is the default; the exchange options can select HTTP text or YAML.
    /// </summary>
    /// <param name="response">The caller-owned, non-null response to capture. It and its request and content objects are read but not disposed.</param>
    /// <param name="exchangeOptions">
    /// Optional capture, redaction, and format options for direct capture during assertion.
    /// For a recorded response, omit this argument or supply the original options instance passed
    /// to the recorder or its Options instance; a separate instance is rejected even when its
    /// values match. Supplying the same instance does not recapture the exchange.
    /// <see langword="null"/> uses recorder options when available, otherwise global or package defaults.
    /// </param>
    /// <param name="snapshotSettings">Optional snapshot settings. <see langword="null"/> uses the effective global or default settings.</param>
    /// <param name="cancellationToken">Token that cancels response reading and snapshot file operations. The default token does not cancel the operation.</param>
    /// <param name="sourceFile">Calling source-file path used to locate and name the snapshot. The compiler supplies this value by default.</param>
    /// <param name="testName">Calling member name used as the default snapshot name. The compiler supplies this value by default.</param>
    /// <returns>A task that completes when the captured exchange matches or is updated; a mismatch writes a received file and throws.</returns>
    /// <exception cref="InvalidOperationException">Separate exchange options were supplied for a response already captured by an HttpExchangeRecorder.</exception>
    public static async Task ShouldMatchHttpExchangeSnapshot(
        this HttpResponseMessage response,
        HttpExchangeSnapshotOptions? exchangeOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        var format = HttpExchangeRecorder.ResolveFormat(response, exchangeOptions);
        var snapshot = await HttpExchangeSnapshot.FromResponseAsync(
            response,
            exchangeOptions,
            cancellationToken);

        await SnapshotAssert.MatchHttpExchangeAsync(
            snapshot,
            format,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }

    /// <summary>Asserts that all exchanges captured by a recorder match their committed snapshot.</summary>
    /// <param name="recorder">The caller-owned, non-null recorder whose completed exchanges are captured in call order. It is not reset or disposed.</param>
    /// <param name="snapshotSettings">Optional snapshot settings. <see langword="null"/> uses the effective global or default settings.</param>
    /// <param name="cancellationToken">Token checked before capture and used by snapshot file operations. The default token does not cancel the operation.</param>
    /// <param name="sourceFile">Calling source-file path used to locate and name the snapshot. The compiler supplies this value by default.</param>
    /// <param name="testName">Calling member name used as the default snapshot name. The compiler supplies this value by default.</param>
    /// <returns>A task that completes when all captured exchanges match or are updated; it throws if a call is incomplete or a snapshot differs.</returns>
    public static async Task ShouldMatchHttpExchangesSnapshot(
        this HttpExchangeRecorder recorder,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var snapshots = await recorder.CreateSnapshotsAsync(cancellationToken);

        await SnapshotAssert.MatchHttpExchangeAsync(
            snapshots,
            recorder.Options.Format,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }
}
