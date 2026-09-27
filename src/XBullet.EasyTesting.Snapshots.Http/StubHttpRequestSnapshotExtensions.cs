using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Snapshot assertions for captured outbound HTTP requests.</summary>
public static class StubHttpRequestSnapshotExtensions
{
    /// <summary>Asserts that one captured outbound request matches its committed JSON snapshot.</summary>
    /// <param name="request">
    /// The non-null captured request converted to a stable model before comparison. It is read but
    /// not owned, retained, or mutated.
    /// </param>
    /// <param name="requestOptions">
    /// Request inclusion, redaction, and scrubbing settings, or <see langword="null"/> to use a new
    /// default instance. Supplied options are read synchronously but not owned or mutated.
    /// </param>
    /// <param name="snapshotSettings">
    /// Naming, storage, serialization, update, and general scrubbing settings. When
    /// <see langword="null"/>, configured global defaults are used, or package defaults when no
    /// global template exists. Supplied settings remain caller-owned.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels snapshot-path locking and file I/O. Cancellation does not mutate or dispose the
    /// captured request or supplied options. The default token does not request cancellation.
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
    /// A task that completes when the snapshot matches or is updated according to the effective
    /// settings. A new or differing snapshot writes a received file before throwing when updates
    /// are disabled.
    /// </returns>
    public static Task ShouldMatchRequestSnapshot(
        this StubHttpRequest request,
        StubHttpRequestSnapshotOptions? requestOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        var snapshot = StubHttpRequestSnapshot.FromRequest(request, requestOptions);
        return SnapshotAssert.MatchAsync(
            snapshot,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }

    /// <summary>Asserts that all requests captured by a stub match their committed JSON snapshot.</summary>
    /// <param name="handler">
    /// The non-null handler whose current request collection is copied before comparison. Requests
    /// recorded after that point are not included. The method does not own or dispose the handler.
    /// </param>
    /// <param name="requestOptions">
    /// Request inclusion, redaction, and scrubbing settings applied to every captured request, or
    /// <see langword="null"/> to use a new default instance. Supplied options remain caller-owned.
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
    /// A task that completes when the request-array snapshot matches or is updated according to the
    /// effective settings. A new or differing snapshot writes a received file before throwing when
    /// updates are disabled.
    /// </returns>
    public static Task ShouldMatchRequestsSnapshot(
        this StubHttpMessageHandler handler,
        StubHttpRequestSnapshotOptions? requestOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(handler);
        var snapshot = handler.Requests
            .Select(request => StubHttpRequestSnapshot.FromRequest(request, requestOptions))
            .ToArray();
        return SnapshotAssert.MatchAsync(
            snapshot,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }
}
