using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Snapshot assertions for captured outbound HTTP exchanges.</summary>
public static class StubHttpExchangeSnapshotExtensions
{
    /// <summary>Asserts that one captured exchange matches its committed snapshot.</summary>
    /// <param name="exchange">
    /// The non-null exchange whose current request, response, and failure state is copied before
    /// comparison. It is read but not owned, retained, or mutated.
    /// </param>
    /// <param name="exchangeOptions">
    /// Request, response, and output-format settings, or <see langword="null"/> to use a new default
    /// instance with JSON format. Supplied options are read but not owned or mutated.
    /// </param>
    /// <param name="snapshotSettings">
    /// Naming, storage, serialization, update, and general scrubbing settings. When
    /// <see langword="null"/>, configured global defaults are used, or package defaults when no
    /// global template exists. Supplied settings remain caller-owned.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels snapshot-path locking and file I/O. Cancellation does not mutate or dispose the
    /// exchange or supplied options. The default token does not request cancellation.
    /// </param>
    /// <param name="sourceFile">
    /// The calling source-file path used to locate and name the snapshot. The compiler supplies it
    /// by default; callers should normally omit it. An empty or invalid path is rejected.
    /// </param>
    /// <param name="testName">
    /// The calling member name used when no explicit snapshot name is configured. The compiler
    /// supplies it by default; callers should normally omit it.
    /// </param>
    /// <returns>
    /// A task that completes when the JSON, HTTP transcript, or YAML snapshot matches or is updated
    /// according to the effective settings. A new or differing snapshot writes a received file
    /// before throwing when updates are disabled.
    /// </returns>
    public static Task ShouldMatchExchangeSnapshot(
        this StubHttpExchange exchange,
        StubHttpExchangeSnapshotOptions? exchangeOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        exchangeOptions ??= new StubHttpExchangeSnapshotOptions();
        var snapshot = StubHttpExchangeSnapshot.FromExchange(exchange, exchangeOptions);
        return SnapshotAssert.MatchHttpExchangeAsync(
            snapshot,
            exchangeOptions.Format,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }

    /// <summary>Asserts that all exchanges captured by a stub match their committed snapshot.</summary>
    /// <param name="handler">
    /// The non-null handler whose current exchange collection is copied before comparison.
    /// Exchanges recorded after that point are not included. The method does not own or dispose the
    /// handler.
    /// </param>
    /// <param name="exchangeOptions">
    /// Request, response, and output-format settings applied to every exchange, or
    /// <see langword="null"/> to use a new default instance with JSON format. Supplied options
    /// remain caller-owned.
    /// </param>
    /// <param name="snapshotSettings">
    /// Naming, storage, serialization, update, and general scrubbing settings. When
    /// <see langword="null"/>, configured global defaults are used, or package defaults when no
    /// global template exists. Supplied settings remain caller-owned.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels snapshot-path locking and file I/O. Cancellation does not mutate or dispose the
    /// handler or supplied options. The default token does not request cancellation.
    /// </param>
    /// <param name="sourceFile">
    /// The calling source-file path used to locate and name the snapshot. The compiler supplies it
    /// by default; callers should normally omit it. An empty or invalid path is rejected.
    /// </param>
    /// <param name="testName">
    /// The calling member name used when no explicit snapshot name is configured. The compiler
    /// supplies it by default; callers should normally omit it.
    /// </param>
    /// <returns>
    /// A task that completes when the exchange-array snapshot matches or is updated according to
    /// the effective settings and selected format. A new or differing snapshot writes a received
    /// file before throwing when updates are disabled.
    /// </returns>
    public static Task ShouldMatchExchangesSnapshot(
        this StubHttpMessageHandler handler,
        StubHttpExchangeSnapshotOptions? exchangeOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(handler);
        exchangeOptions ??= new StubHttpExchangeSnapshotOptions();
        var snapshot = handler.Exchanges
            .Select(exchange => StubHttpExchangeSnapshot.FromExchange(exchange, exchangeOptions))
            .ToArray();
        return SnapshotAssert.MatchHttpExchangeAsync(
            snapshot,
            exchangeOptions.Format,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }
}
