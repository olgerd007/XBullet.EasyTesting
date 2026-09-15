using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Snapshot assertions for captured outbound HTTP requests.</summary>
public static class StubHttpRequestSnapshotExtensions
{
    /// <summary>Asserts that one captured outbound request matches its committed JSON snapshot.</summary>
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
