using System.Runtime.CompilerServices;

namespace XBullet.EasyTesting.Snapshots;

#pragma warning disable RS0026 // Required variant and callback distinguish these caller-info overloads.

/// <summary>Fluent snapshot assertions for serializable values.</summary>
public static class SnapshotAssertionExtensions
{
    /// <summary>Serializes a value and compares it with its committed JSON snapshot.</summary>
    /// <param name="actual">The value to serialize, including <see langword="null"/>.</param>
    /// <param name="snapshotSettings">Optional settings merged with global or package defaults.</param>
    /// <param name="cancellationToken">Token that cancels snapshot locking and asynchronous file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes after a match or allowed update; otherwise throws <see cref="SnapshotMismatchException"/>.</returns>
    public static Task ShouldMatchSnapshot(
        this object? actual,
        SnapshotSettings? snapshotSettings = null,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        SnapshotAssert.MatchAsync(actual, snapshotSettings, cancellationToken, sourceFile, testName);

    /// <summary>Serializes a value and compares it with a distinct JSON snapshot variant.</summary>
    /// <param name="actual">The value to serialize, including <see langword="null"/>.</param>
    /// <param name="variant">The non-empty variant distinguishing snapshots within the calling test.</param>
    /// <param name="cancellationToken">Token that cancels snapshot locking and asynchronous file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes after a match or allowed update; otherwise throws <see cref="SnapshotMismatchException"/>.</returns>
    public static Task ShouldMatchSnapshot(
        this object? actual,
        string variant,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        SnapshotAssert.MatchAsync(
            actual,
            new SnapshotSettings().ForVariant(variant),
            cancellationToken,
            sourceFile,
            testName);

    /// <summary>Configures independent snapshot settings inline and asserts a serialized value.</summary>
    /// <param name="actual">The value to serialize, including <see langword="null"/>.</param>
    /// <param name="configureSnapshot">A non-null callback applied once to settings extended from global or package defaults.</param>
    /// <param name="cancellationToken">Token that cancels snapshot locking and asynchronous file operations.</param>
    /// <param name="sourceFile">Calling source-file path supplied by the compiler for snapshot placement.</param>
    /// <param name="testName">Calling member name supplied by the compiler for snapshot naming.</param>
    /// <returns>A task that completes after a match or allowed update; otherwise throws <see cref="SnapshotMismatchException"/>.</returns>
    public static Task ShouldMatchSnapshot(
        this object? actual,
        Action<SnapshotSettings> configureSnapshot,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string sourceFile = "",
        [CallerMemberName] string testName = "") =>
        SnapshotAssert.MatchAsync(
            actual,
            SnapshotSettingsDefaults.ExtendGlobal(configureSnapshot),
            cancellationToken,
            sourceFile,
            testName);
}

#pragma warning restore RS0026
