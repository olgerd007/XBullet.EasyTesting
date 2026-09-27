# Review, accept, and maintain snapshots

Treat every mismatch as a code review event. The safe default is verification-only: write a
received file, fail the test, inspect the difference, and accept deliberately.

## Read mismatch diagnostics

JSON mismatches report the first structural difference with a JSONPath and compact expected and
actual values:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-mismatch-diagnostics -->
```csharp
[Fact]
public async Task Mismatch_reports_first_structural_json_path_and_values()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = CreateUpdatingSettings(snapshotDirectory).Named("diagnostics");

    try
    {
        await SnapshotAssert.MatchAsync(
            new { Order = new { Items = new[] { new { Id = 1, Name = "Keyboard" } } } },
            settings,
            cancellationToken);
        settings.Updating(SnapshotUpdateMode.None);

        var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
            SnapshotAssert.MatchAsync(
                new { Order = new { Items = new[] { new { Id = 2, Name = "Keyboard" } } } },
                settings,
                cancellationToken));

        Assert.Equal("$.Order.Items[0].Id", exception.DifferencePath);
        Assert.Equal("1", exception.ExpectedValue);
        Assert.Equal("2", exception.ActualValue);
        Assert.Contains("$.Order.Items[0].Id", exception.Message);
        Assert.Contains("expected 1; actual 2", exception.Message);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

The same values are available through `DifferencePath`, `ExpectedValue`, and `ActualValue` on
`SnapshotMismatchException`.

On a local non-CI run, the engine can open a detected Visual Studio, VS Code, Rider, or Meld diff
viewer. Disable it with `WithoutDiffTool()` or select one with
`WithDiffTool(SnapshotDiffTool.VisualStudioCode())`.

## Accept one reviewed snapshot

Promote the received path carried by the exception:

```csharp
var verifiedPath = SnapshotAssert.AcceptReceived(exception.ReceivedPath);
```

Review the received content before calling this. Acceptance replaces the corresponding verified
file when one exists.

## Automatic update modes

`Missing` creates absent verified files; `All` also replaces changed verified files:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-update-modes -->
```csharp
[Fact]
public async Task Automatic_update_modes_create_and_replace_verified_snapshots()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = new SnapshotSettings
    {
        Directory = snapshotDirectory,
        SnapshotName = "automatic-update",
        UpdateMode = SnapshotUpdateMode.Missing,
        AllowUpdatesInContinuousIntegration = true,
        LaunchDiffTool = false
    };

    try
    {
        await SnapshotAssert.MatchAsync(new { Version = 1 }, settings, cancellationToken);

        settings.UpdateMode = SnapshotUpdateMode.None;
        await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
            SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken));

        settings.UpdateMode = SnapshotUpdateMode.All;
        await SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken);

        settings.UpdateMode = SnapshotUpdateMode.None;
        await SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Select a mode in code with `Updating`, or for a test run with:

```text
INTEGRATION_TESTS_UPDATE_SNAPSHOTS=missing
INTEGRATION_TESTS_UPDATE_SNAPSHOTS=all
```

Never use `all` in a normal verification job. It can silently replace reviewed contracts.

## CI write protection

CI rejects automatic writes unless a second opt-in is present:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-ci-guard -->
```csharp
[Fact]
public async Task Automatic_updates_in_ci_require_separate_explicit_opt_in()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var originalCi = Environment.GetEnvironmentVariable("CI");
    var originalOptIn = Environment.GetEnvironmentVariable(
        SnapshotSettings.AllowCiUpdatesEnvironmentVariable);

    try
    {
        Environment.SetEnvironmentVariable("CI", "true");
        Environment.SetEnvironmentVariable(
            SnapshotSettings.AllowCiUpdatesEnvironmentVariable,
            null);
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("ci-guard")
            .Updating(SnapshotUpdateMode.Missing)
            .WithoutDiffTool();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken));

        Assert.Contains(
            SnapshotSettings.AllowCiUpdatesEnvironmentVariable,
            exception.Message);
        Assert.Empty(Directory.EnumerateFiles(snapshotDirectory));

        settings.AllowingUpdatesInContinuousIntegration();
        await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
        Assert.Single(Directory.EnumerateFiles(snapshotDirectory, "*.verified.json"));
    }
    finally
    {
        Environment.SetEnvironmentVariable("CI", originalCi);
        Environment.SetEnvironmentVariable(
            SnapshotSettings.AllowCiUpdatesEnvironmentVariable,
            originalOptIn);
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

A dedicated update job may set:

```text
INTEGRATION_TESTS_UPDATE_SNAPSHOTS=all
INTEGRATION_TESTS_ALLOW_SNAPSHOT_UPDATES_IN_CI=true
```

Code can use `AllowingUpdatesInContinuousIntegration()`. Restrict either mechanism to an isolated
job that produces a reviewable change; never enable it for ordinary pull-request verification.

## Bulk acceptance and removal

Preview first, then pass explicit confirmation:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-bulk-maintenance -->
```csharp
[Fact]
public async Task Bulk_maintenance_requires_preview_and_explicit_confirmation()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var nestedDirectory = Path.Combine(snapshotDirectory, "nested");
    Directory.CreateDirectory(nestedDirectory);
    var firstReceived = Path.Combine(snapshotDirectory, "First.received.json");
    var secondReceived = Path.Combine(nestedDirectory, "Second.received.json");
    var thirdReceived = Path.Combine(nestedDirectory, "Third.received.net8.0.txt");
    await File.WriteAllTextAsync(firstReceived, "{}", cancellationToken);
    await File.WriteAllTextAsync(secondReceived, "{}", cancellationToken);
    await File.WriteAllTextAsync(thirdReceived, "text", cancellationToken);

    try
    {
        var preview = SnapshotMaintenance.FindReceivedSnapshots(snapshotDirectory);
        Assert.Equal(3, preview.Count);

        Assert.Throws<InvalidOperationException>(() =>
            SnapshotMaintenance.AcceptReceivedSnapshots(snapshotDirectory));
        Assert.All(preview, path => Assert.True(File.Exists(path)));

        var accepted = SnapshotMaintenance.AcceptReceivedSnapshots(
            snapshotDirectory,
            confirmed: true);
        Assert.Equal(3, accepted.Count);
        Assert.All(accepted, path => Assert.True(File.Exists(path)));

        Assert.Throws<InvalidOperationException>(() =>
            SnapshotMaintenance.RemoveVerifiedSnapshots(accepted));
        var removed = SnapshotMaintenance.RemoveVerifiedSnapshots(
            accepted,
            confirmed: true);

        Assert.Equal(accepted, removed);
        Assert.All(removed, path => Assert.False(File.Exists(path)));
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

`AcceptReceivedSnapshots` and `RemoveVerifiedSnapshots` refuse to mutate files unless
`confirmed: true` is supplied. Removal validates the entire input before deleting any file.

## Detect obsolete verified files

Track observed snapshots with `SnapshotCatalog`, then compare them with a directory:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-obsolete -->
```csharp
[Fact]
public async Task Catalog_detects_verified_snapshots_not_observed_by_the_test_run()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var catalog = new SnapshotCatalog();
    var settings = CreateUpdatingSettings(snapshotDirectory)
        .Named("active")
        .TrackingWith(catalog);

    try
    {
        await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
        var obsoletePath = Path.Combine(snapshotDirectory, "OldTests.Removed.verified.json");
        var obsoleteTextPath = Path.Combine(snapshotDirectory, "OldTests.Removed.verified.txt");
        await File.WriteAllTextAsync(obsoletePath, "{}", cancellationToken);
        await File.WriteAllTextAsync(obsoleteTextPath, "old", cancellationToken);

        var obsolete = catalog.FindObsoleteSnapshots(snapshotDirectory);

        Assert.Single(catalog.ObservedVerifiedSnapshots);
        Assert.Equal(
            [Path.GetFullPath(obsoletePath), Path.GetFullPath(obsoleteTextPath)],
            obsolete);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Run this audit only after the complete intended test scope passes. A filtered, skipped, cancelled,
or failed run makes unexecuted but valid snapshots appear obsolete. Always review the returned list
before confirmed removal.

## Repository policy

- Commit verified files and exclude received files.
- Keep update environment variables unset in normal CI.
- Fail on leftover received files when the test workflow supports that check.
- Review sensitive-data transformations before accepting.
- Pair snapshot changes with the implementation change that caused them.

See the [snapshot core API reference](../../api/packages/xbullet-easytesting-snapshots-core.md).
