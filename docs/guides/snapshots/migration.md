# Migrate snapshot package references

The original combined `XBullet.EasyTesting.Snapshots` package is now a compatibility facade over a
framework-independent core package and an outbound HTTP adapter package. Public types keep the
`XBullet.EasyTesting.Snapshots` namespace.

## Choose the replacement

| Existing usage | Package for new projects |
| --- | --- |
| Values, raw JSON, text, HTTP content, controller responses, or `HttpExchangeRecorder` | `XBullet.EasyTesting.Snapshots.Core` |
| Requests or exchanges from `StubHttpMessageHandler` | `XBullet.EasyTesting.Snapshots.Http` |
| Both groups | `XBullet.EasyTesting.Snapshots.Http` already references core |

The split is verified by forwarded public types and dependency boundaries:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-compatibility -->
```csharp
[Fact]
public void Compatibility_facade_forwards_to_split_snapshot_assemblies()
{
    var facade = System.Reflection.Assembly.Load("XBullet.EasyTesting.Snapshots");
    var forwardedTypes = facade.GetForwardedTypes();

    Assert.Contains(typeof(SnapshotAssert), forwardedTypes);
    Assert.Contains(typeof(SnapshotLocationContext), forwardedTypes);
    Assert.Contains(typeof(ControllerSnapshotOptionsDefaults), forwardedTypes);
    Assert.Contains(typeof(StubHttpRequestSnapshot), forwardedTypes);
    Assert.Contains(typeof(StubHttpExchangeSnapshot), forwardedTypes);
    Assert.Contains(typeof(HttpExchangeRecorder), forwardedTypes);
    Assert.Contains(typeof(HttpExchangeFailureSnapshot), forwardedTypes);
    Assert.Contains(typeof(HttpExchangeSnapshotFormat), forwardedTypes);
    Assert.Contains(typeof(HttpExchangeSnapshotOptionsDefaults), forwardedTypes);
    Assert.Contains(typeof(HttpResponseSnapshotConfigurationExtensions), forwardedTypes);
    Assert.Contains(typeof(StubHttpSnapshotConfigurationExtensions), forwardedTypes);
    Assert.Contains(typeof(TestScenarioSnapshotExtensions), forwardedTypes);
    Assert.Equal(
        "XBullet.EasyTesting.Snapshots.Core",
        typeof(SnapshotAssert).Assembly.GetName().Name);
    Assert.Equal(
        "XBullet.EasyTesting.Snapshots.Http",
        typeof(StubHttpRequestSnapshot).Assembly.GetName().Name);

    var coreReferences = typeof(SnapshotAssert).Assembly
        .GetReferencedAssemblies()
        .Select(reference => reference.Name)
        .ToArray();
    Assert.DoesNotContain("XBullet.EasyTesting.Http", coreReferences);
    Assert.DoesNotContain("XBullet.EasyTesting", coreReferences);
}
```
<!-- end-snippet -->

Core has no dependency on `XBullet.EasyTesting`, ASP.NET testing, or
`XBullet.EasyTesting.Http`. This keeps non-HTTP snapshot projects lightweight.

## Migrate a project

For core-only usage:

```shell
dotnet remove package XBullet.EasyTesting.Snapshots
dotnet add package XBullet.EasyTesting.Snapshots.Core
```

For outbound HTTP stub snapshots:

```shell
dotnet remove package XBullet.EasyTesting.Snapshots
dotnet add package XBullet.EasyTesting.Snapshots.Http
```

Then restore, build, and run the snapshot suite on every target framework. Source changes are
normally unnecessary because namespaces and public APIs remain stable. Existing verified files do
not need renaming solely because of the package split.

If a transitive dependency previously supplied the combined package, add the specific replacement
directly to the test project so its snapshot capability is explicit.

## Stay on the facade temporarily

Existing projects may retain `XBullet.EasyTesting.Snapshots`. The facade references both split
packages and forwards their public types, preserving binary compatibility. It remains useful for a
staged migration, but it also keeps the outbound HTTP dependency graph even when only core
snapshots are used.

Do not mix facade and specific package versions arbitrarily. Keep all XBullet snapshot packages on
the same release version to avoid assembly-resolution surprises.

## Verify.Xunit is separate

`XBullet.EasyTesting.Verify.Xunit` is not a replacement assembly for the built-in engine; it is an
optional adapter to Verify.Xunit v3. Its acceptance workflow and verified-file format remain owned
by Verify. See [choose an engine](choose-engine.md).

The [API package index](../../api/index.md) identifies the canonical reference page for each
implementation package and the compatibility facade.
