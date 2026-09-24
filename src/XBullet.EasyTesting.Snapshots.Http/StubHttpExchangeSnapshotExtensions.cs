using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

/// <summary>Snapshot assertions for captured outbound HTTP exchanges.</summary>
public static class StubHttpExchangeSnapshotExtensions
{
    /// <summary>Asserts that one captured exchange matches its committed JSON snapshot.</summary>
    public static Task ShouldMatchExchangeSnapshot(
        this StubHttpExchange exchange,
        StubHttpExchangeSnapshotOptions? exchangeOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        var snapshot = StubHttpExchangeSnapshot.FromExchange(exchange, exchangeOptions);
        return SnapshotAssert.MatchAsync(
            snapshot,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }

    /// <summary>Asserts that all exchanges captured by a stub match their committed JSON snapshot.</summary>
    public static Task ShouldMatchExchangesSnapshot(
        this StubHttpMessageHandler handler,
        StubHttpExchangeSnapshotOptions? exchangeOptions = null,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "")
    {
        ArgumentNullException.ThrowIfNull(handler);
        var snapshot = handler.Exchanges
            .Select(exchange => StubHttpExchangeSnapshot.FromExchange(exchange, exchangeOptions))
            .ToArray();
        return SnapshotAssert.MatchAsync(
            snapshot,
            snapshotSettings,
            cancellationToken,
            sourceFile,
            testName);
    }
}
