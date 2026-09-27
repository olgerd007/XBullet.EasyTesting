# Snapshot files, names, and parallel runs

The built-in engine derives a stable verified path from the calling source file, test name,
optional snapshot name, optional variant, and output format.

## Default and custom locations

By default, files are placed in `__snapshots__` beside the calling source file. Configure another
location with `SnapshotSettings`:

```csharp
var besideSource = new SnapshotSettings().BesideSourceFile();

var relative = new SnapshotSettings().InDirectory("snapshots");

var centralized = new SnapshotSettings().InDirectory(context => Path.Combine(
    context.SourceDirectory,
    "snapshots",
    context.SourceFileName));
```

Relative paths are resolved from the calling source file, not the process working directory. The
callback receives source directory, source filename, test name, snapshot name, and variant. Use a
fully qualified directory when SourceLink rewrites caller paths in CI.

## Names and variants

`Named` replaces the default method-derived snapshot name. Use `ForVariant` when one test method
produces more than one snapshot:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-variants -->
```csharp
[Fact]
public async Task Snapshot_variants_create_distinct_files_for_one_test()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();

    try
    {
        await SnapshotAssert.MatchAsync(
            new { Status = "accepted" },
            CreateUpdatingSettings(snapshotDirectory).ForVariant("accepted"),
            cancellationToken);
        await SnapshotAssert.MatchAsync(
            new { Status = "rejected" },
            CreateUpdatingSettings(snapshotDirectory).ForVariant("rejected"),
            cancellationToken);

        var fileNames = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, fileNames.Length);
        Assert.Contains(fileNames, name => name!.Contains(".accepted.verified.json", StringComparison.Ordinal));
        Assert.Contains(fileNames, name => name!.Contains(".rejected.verified.json", StringComparison.Ordinal));
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Variants must be stable and unique within the test. Parameterized tests should derive one from the
case identity. Use `ForHashedVariant(parameters)` when raw parameter values are sensitive, long, or
not portable as filenames.

Names are escaped for portability, Windows device names are protected, and overly long names are
shortened with a deterministic hash. Names that differ only by case are rejected because they
collide on common filesystems.

## Extensions and runtime-qualified received files

| Snapshot | Verified | Received example |
| --- | --- | --- |
| JSON and structural values | `.verified.json` | `.received.net10.0.json` |
| Plain text or HTTP transcript | `.verified.txt` | `.received.net10.0.txt` |
| YAML HTTP exchange | `.verified.yaml` | `.received.net10.0.yaml` |

A passing target removes only its own matching received file. It does not delete received output
from another target framework. This allows multi-target test projects to run concurrently while
sharing one reviewed verified contract.

## Concurrency

Matches targeting the same verified path are coordinated inside the process. Do not deliberately
give unrelated tests the same path: coordination prevents file corruption, but two tests still
cannot own different contracts under one name. Give each parameter case or logical assertion a
unique variant.

Next: [snapshot recipes](recipes.md) and [maintenance and CI](maintenance.md).

See the [snapshot core API reference](../../api/packages/xbullet-easytesting-snapshots-core.md).
