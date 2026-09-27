# Configure snapshot defaults

Shared defaults keep redaction, location, formatting, and diff behavior consistent. Configure
global defaults once before test discovery, or use an instance template for one fixture or suite.

## Assembly-wide defaults

A module initializer is a convenient place to configure the three global templates:

```csharp
using System.Runtime.CompilerServices;
using XBullet.EasyTesting.Snapshots;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettingsDefaults.Global = new(settings => settings
            .ScrubGuids()
            .ScrubDateTimes()
            .ScrubMembers("RequestId", "CorrelationId")
            .IgnoreMembers("AccessToken")
            .CanonicalizeJson());

        ControllerSnapshotOptionsDefaults.Global = new(options => options
            .IgnoringHeaders("ETag", "X-Request-Nonce")
            .RedactingHeaders("Authorization", "X-Session-Token"));

        HttpExchangeSnapshotOptionsDefaults.Global = new(options =>
        {
            options.Format = HttpExchangeSnapshotFormat.Yaml;
            options.Request.IgnoringHeaders("X-Request-Nonce");
            options.Response.IgnoringHeaders("ETag");
        });
    }
}
```

Each assertion receives an independent copy. The global object is a template, not mutable state
shared by parallel tests.

## Merge and precedence

Explicit settings merge over global defaults. Collection rules—scrubbers, member rules, path rules,
and header/query decisions—are combined. Locally configured scalar values such as name, directory,
format, or update mode take precedence. For the same header or query name, the explicit local
decision wins:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-global-defaults -->
```csharp
[Fact]
public async Task Global_defaults_merge_with_explicit_settings_and_local_values_take_precedence()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var globalDirectory = CreateTemporarySnapshotDirectory();
    var explicitDirectory = CreateTemporarySnapshotDirectory();
    var originalDefaults = SnapshotSettingsDefaults.Global;

    try
    {
        SnapshotSettingsDefaults.Global = new(settings => settings
            .InDirectory(globalDirectory)
            .Named("global-defaults")
            .ScrubMember("Id")
            .Updating(SnapshotUpdateMode.Missing)
            .AllowingUpdatesInContinuousIntegration()
            .WithoutDiffTool());

        await SnapshotAssert.MatchAsync(
            new { Id = 123, Name = "global" },
            cancellationToken: cancellationToken);
        await SnapshotAssert.MatchAsync(
            new { Id = 456, Timestamp = "volatile", Name = "explicit" },
            new SnapshotSettings()
                .InDirectory(explicitDirectory)
                .Named("explicit-settings")
                .ScrubMember("Timestamp"),
            cancellationToken);

        var globalSnapshot = await ReadSingleVerifiedSnapshotAsync(
            globalDirectory,
            cancellationToken);
        var explicitSnapshot = await ReadSingleVerifiedSnapshotAsync(
            explicitDirectory,
            cancellationToken);

        Assert.Contains("{Scrubbed}", globalSnapshot);
        Assert.DoesNotContain("123", globalSnapshot);
        Assert.Contains("{Scrubbed}", explicitSnapshot);
        Assert.DoesNotContain("456", explicitSnapshot);
        Assert.DoesNotContain("volatile", explicitSnapshot);
    }
    finally
    {
        SnapshotSettingsDefaults.Global = originalDefaults;
        DeleteTemporarySnapshotDirectory(globalDirectory);
        DeleteTemporarySnapshotDirectory(explicitDirectory);
    }
}
```
<!-- end-snippet -->

Use `SnapshotSettingsDefaults.ExtendGlobal`,
`ControllerSnapshotOptionsDefaults.ExtendGlobal`, or
`HttpExchangeSnapshotOptionsDefaults.ExtendGlobal` when code needs an independent configured copy
before calling an assertion or constructing a recorder.

Set a `Global` property to `null` to restore package defaults. This is mainly useful when a test
isolates configuration; production test suites should configure globals once.

## Fixture-local reusable defaults

Create an instance template when conventions belong to only one fixture or feature:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-reusable-defaults -->
```csharp
[Fact]
public async Task Project_defaults_create_independent_settings_with_local_overrides()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var catalog = new SnapshotCatalog();
    var defaults = new SnapshotSettingsDefaults(settings => settings
        .InDirectory(snapshotDirectory)
        .ScrubMember("Id")
        .Updating(SnapshotUpdateMode.Missing)
        .AllowingUpdatesInContinuousIntegration()
        .TrackingWith(catalog)
        .WithoutDiffTool());
    var first = defaults.Create(settings => settings.Named("first"));
    var second = defaults.Create(settings => settings.Named("second"));

    try
    {
        first.IgnoreMember("OnlyFirst");
        await SnapshotAssert.MatchAsync(
            new { Id = 123, OnlyFirst = "hidden" },
            first,
            cancellationToken);
        await SnapshotAssert.MatchAsync(
            new { Id = 456, OnlyFirst = "visible" },
            second,
            cancellationToken);

        var snapshots = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json")
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Equal(2, snapshots.Length);
        Assert.All(snapshots, snapshot => Assert.Contains("{Scrubbed}", snapshot));
        Assert.Contains(snapshots, snapshot => snapshot.Contains("visible", StringComparison.Ordinal));
        Assert.Contains(snapshots, snapshot => !snapshot.Contains("OnlyFirst", StringComparison.Ordinal));
        Assert.Equal(2, catalog.ObservedVerifiedSnapshots.Count);
        Assert.NotSame(first.JsonSerializerOptions, second.JsonSerializerOptions);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Every `Create` call returns a separate settings instance, so one test can add local rules without
affecting another. A template can also attach a `SnapshotCatalog` for later obsolete-file auditing.

Keep safety-sensitive exclusions and redactions in the broadest applicable template. Add
case-specific naming, variants, and narrow scrubbers locally.

See the [snapshot core API reference](../../api/packages/xbullet-easytesting-snapshots-core.md).
