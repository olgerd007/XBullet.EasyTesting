using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Http;

namespace XBullet.EasyTesting.Snapshots;

#pragma warning disable RS0026 // Required callbacks distinguish these caller-info overloads.

/// <summary>Callback-configured overloads for outbound HTTP stub snapshot assertions.</summary>
public static class StubHttpSnapshotConfigurationExtensions
{
    /// <summary>Configures snapshot settings inline and asserts one captured request.</summary>
    /// <param name="request">The captured request to snapshot.</param>
    /// <param name="configureSnapshot">A callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchRequestSnapshot(
        this StubHttpRequest request,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        request.ShouldMatchRequestSnapshot(
            snapshotSettings: CreateSnapshotSettings(configureSnapshot),
            cancellationToken: cancellationToken,
            sourceFile: sourceFile,
            testName: testName);

    /// <summary>Configures request options and optional snapshot settings inline.</summary>
    /// <param name="request">The captured request to snapshot.</param>
    /// <param name="configureRequest">A callback that configures request snapshot options.</param>
    /// <param name="configureSnapshot">An optional callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchRequestSnapshot(
        this StubHttpRequest request,
        Action<StubHttpRequestSnapshotOptions> configureRequest,
        Action<SnapshotSettings>? configureSnapshot = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        request.ShouldMatchRequestSnapshot(
            CreateRequestOptions(configureRequest),
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts all captured requests.</summary>
    /// <param name="handler">The handler whose captured requests are snapshotted.</param>
    /// <param name="configureSnapshot">A callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchRequestsSnapshot(
        this StubHttpMessageHandler handler,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        handler.ShouldMatchRequestsSnapshot(
            snapshotSettings: CreateSnapshotSettings(configureSnapshot),
            cancellationToken: cancellationToken,
            sourceFile: sourceFile,
            testName: testName);

    /// <summary>Configures request options and optional snapshot settings inline.</summary>
    /// <param name="handler">The handler whose captured requests are snapshotted.</param>
    /// <param name="configureRequest">A callback that configures request snapshot options.</param>
    /// <param name="configureSnapshot">An optional callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchRequestsSnapshot(
        this StubHttpMessageHandler handler,
        Action<StubHttpRequestSnapshotOptions> configureRequest,
        Action<SnapshotSettings>? configureSnapshot = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        handler.ShouldMatchRequestsSnapshot(
            CreateRequestOptions(configureRequest),
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts one captured exchange.</summary>
    /// <param name="exchange">The captured exchange to snapshot.</param>
    /// <param name="configureSnapshot">A callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchExchangeSnapshot(
        this StubHttpExchange exchange,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        exchange.ShouldMatchExchangeSnapshot(
            snapshotSettings: CreateSnapshotSettings(configureSnapshot),
            cancellationToken: cancellationToken,
            sourceFile: sourceFile,
            testName: testName);

    /// <summary>Configures exchange options and optional snapshot settings inline.</summary>
    /// <param name="exchange">The captured exchange to snapshot.</param>
    /// <param name="configureExchange">A callback that configures exchange snapshot options.</param>
    /// <param name="configureSnapshot">An optional callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchExchangeSnapshot(
        this StubHttpExchange exchange,
        Action<StubHttpExchangeSnapshotOptions> configureExchange,
        Action<SnapshotSettings>? configureSnapshot = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        exchange.ShouldMatchExchangeSnapshot(
            CreateExchangeOptions(configureExchange),
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts all captured exchanges.</summary>
    /// <param name="handler">The handler whose captured exchanges are snapshotted.</param>
    /// <param name="configureSnapshot">A callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchExchangesSnapshot(
        this StubHttpMessageHandler handler,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        handler.ShouldMatchExchangesSnapshot(
            snapshotSettings: CreateSnapshotSettings(configureSnapshot),
            cancellationToken: cancellationToken,
            sourceFile: sourceFile,
            testName: testName);

    /// <summary>Configures exchange options and optional snapshot settings inline.</summary>
    /// <param name="handler">The handler whose captured exchanges are snapshotted.</param>
    /// <param name="configureExchange">A callback that configures exchange snapshot options.</param>
    /// <param name="configureSnapshot">An optional callback that configures independent effective snapshot settings.</param>
    /// <param name="cancellationToken">A token that cancels snapshot locking and file operations.</param>
    /// <param name="sourceFile">The compiler-supplied source file used to locate the snapshot.</param>
    /// <param name="testName">The compiler-supplied test name used to name the snapshot.</param>
    /// <returns>A task that completes when the snapshot has been verified or updated.</returns>
    public static Task ShouldMatchExchangesSnapshot(
        this StubHttpMessageHandler handler,
        Action<StubHttpExchangeSnapshotOptions> configureExchange,
        Action<SnapshotSettings>? configureSnapshot = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        handler.ShouldMatchExchangesSnapshot(
            CreateExchangeOptions(configureExchange),
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    private static StubHttpRequestSnapshotOptions CreateRequestOptions(
        Action<StubHttpRequestSnapshotOptions> configureRequest)
    {
        ArgumentNullException.ThrowIfNull(configureRequest);
        var options = new StubHttpRequestSnapshotOptions();
        configureRequest(options);
        return options;
    }

    private static StubHttpExchangeSnapshotOptions CreateExchangeOptions(
        Action<StubHttpExchangeSnapshotOptions> configureExchange)
    {
        ArgumentNullException.ThrowIfNull(configureExchange);
        var options = new StubHttpExchangeSnapshotOptions();
        configureExchange(options);
        return options;
    }

    private static SnapshotSettings? CreateSnapshotSettings(
        Action<SnapshotSettings>? configureSnapshot) =>
        configureSnapshot is null
            ? null
            : SnapshotSettingsDefaults.ExtendGlobal(configureSnapshot);
}

#pragma warning restore RS0026
