using System.Runtime.CompilerServices;

namespace XBullet.EasyTesting.Snapshots;

#pragma warning disable RS0026 // Required callbacks distinguish these caller-info overloads.

/// <summary>Callback-configured overloads for built-in HTTP snapshot assertions.</summary>
public static class HttpResponseSnapshotConfigurationExtensions
{
    /// <summary>Configures snapshot settings inline and asserts normalized JSON content.</summary>
    /// <param name="content">The non-null caller-owned HTTP content to read without disposing it.</param>
    /// <param name="configureSnapshot">The non-null callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token that cancels content reading and snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when the normalized JSON matches or is updated.</returns>
    public static Task ShouldMatchJsonSnapshot(
        this HttpContent content,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        content.ShouldMatchJsonSnapshot(
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts a normalized JSON response body.</summary>
    /// <param name="response">The non-null caller-owned response whose content is read.</param>
    /// <param name="configureSnapshot">The non-null callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token that cancels response reading and snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when the normalized response body matches or is updated.</returns>
    public static Task ShouldMatchJsonBodySnapshot(
        this HttpResponseMessage response,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        response.ShouldMatchJsonBodySnapshot(
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts a controller response.</summary>
    /// <param name="response">The non-null caller-owned response to capture without disposing it.</param>
    /// <param name="configureSnapshot">The non-null callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token that cancels response reading and snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when the controller snapshot matches or is updated.</returns>
    public static Task ShouldMatchControllerSnapshot(
        this HttpResponseMessage response,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        response.ShouldMatchControllerSnapshot(
            snapshotSettings: CreateSnapshotSettings(configureSnapshot),
            cancellationToken: cancellationToken,
            sourceFile: sourceFile,
            testName: testName);

    /// <summary>Configures controller options and optional snapshot settings inline.</summary>
    /// <param name="response">The non-null caller-owned response to capture without disposing it.</param>
    /// <param name="configureController">The non-null callback applied once to independent effective controller options.</param>
    /// <param name="configureSnapshot">An optional callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token that cancels response reading and snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when the configured controller snapshot matches or is updated.</returns>
    public static Task ShouldMatchControllerSnapshot(
        this HttpResponseMessage response,
        Action<ControllerSnapshotOptions> configureController,
        Action<SnapshotSettings>? configureSnapshot = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        response.ShouldMatchControllerSnapshot(
            ControllerSnapshotOptionsDefaults.ExtendGlobal(configureController),
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts a complete HTTP exchange.</summary>
    /// <param name="response">The non-null caller-owned response representing the exchange.</param>
    /// <param name="configureSnapshot">The non-null callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token that cancels exchange reading and snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when the complete exchange snapshot matches or is updated.</returns>
    public static Task ShouldMatchHttpExchangeSnapshot(
        this HttpResponseMessage response,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        response.ShouldMatchHttpExchangeSnapshot(
            snapshotSettings: CreateSnapshotSettings(configureSnapshot),
            cancellationToken: cancellationToken,
            sourceFile: sourceFile,
            testName: testName);

    /// <summary>Configures exchange options and optional snapshot settings inline.</summary>
    /// <remarks>
    /// Configure recorder-backed responses when creating the recorder or snapshot scenario. A
    /// recorded response does not accept replacement exchange options during assertion.
    /// </remarks>
    /// <param name="response">The non-null caller-owned response representing the exchange.</param>
    /// <param name="configureExchange">The non-null callback applied once to independent effective exchange options.</param>
    /// <param name="configureSnapshot">An optional callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token that cancels exchange reading and snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when the configured exchange snapshot matches or is updated.</returns>
    public static Task ShouldMatchHttpExchangeSnapshot(
        this HttpResponseMessage response,
        Action<HttpExchangeSnapshotOptions> configureExchange,
        Action<SnapshotSettings>? configureSnapshot = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        response.ShouldMatchHttpExchangeSnapshot(
            HttpExchangeSnapshotOptionsDefaults.ExtendGlobal(configureExchange),
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures snapshot settings inline and asserts all recorder exchanges.</summary>
    /// <param name="recorder">The non-null caller-owned recorder whose completed exchanges are copied.</param>
    /// <param name="configureSnapshot">The non-null callback applied once to independent effective snapshot settings.</param>
    /// <param name="cancellationToken">Token checked before capture and used for snapshot file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes when all recorded exchange snapshots match or are updated.</returns>
    public static Task ShouldMatchHttpExchangesSnapshot(
        this HttpExchangeRecorder recorder,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        recorder.ShouldMatchHttpExchangesSnapshot(
            CreateSnapshotSettings(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);

    private static SnapshotSettings? CreateSnapshotSettings(
        Action<SnapshotSettings>? configureSnapshot) =>
        configureSnapshot is null
            ? null
            : SnapshotSettingsDefaults.ExtendGlobal(configureSnapshot);
}

#pragma warning restore RS0026
