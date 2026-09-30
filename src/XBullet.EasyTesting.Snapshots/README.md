# XBullet.EasyTesting.Snapshots.Core

Framework-independent snapshot assertions for serializable values, raw JSON, plain text, HTTP
content, controller responses, and complete request/response exchanges.

The package targets .NET 8, .NET 9, and .NET 10. It does not depend on xUnit, Verify,
`XBullet.EasyTesting`, ASP.NET testing, or `XBullet.EasyTesting.Http`.

## Install

```shell
dotnet add package XBullet.EasyTesting.Snapshots.Core
```

Use `XBullet.EasyTesting.Snapshots.Http` instead when the test snapshots requests or exchanges
captured by `StubHttpMessageHandler`. Existing references to `XBullet.EasyTesting.Snapshots` can
remain temporarily through the compatibility facade.

## Example

```csharp
var settings = new SnapshotSettings()
    .Named("administrator-order")
    .ScrubMembers("Id", "CreatedAt")
    .IgnoreMember("AccessToken")
    .ScrubGuids();

await SnapshotAssert.MatchAsync(result, settings, cancellationToken);
```

The first verification failure writes a target-framework-qualified `*.received.*` file. Review it,
ensure dynamic and sensitive values are handled, and promote it to the shared `*.verified.*` file.
Commit verified files; do not commit received files.

Controller and complete TestServer exchange assertions use the same settings:

```csharp
await response.ShouldMatchControllerSnapshot(
    snapshotSettings: settings,
    cancellationToken: cancellationToken);

await response.ShouldMatchHttpExchangeSnapshot(
    configureSnapshot: settings => settings.ScrubMember("id"),
    cancellationToken: cancellationToken);
```

Snapshot assertions also continue to accept prebuilt `SnapshotSettings` instances.

Attach `HttpExchangeRecorder` to the real client pipeline when the complete request body must be
captured before TestServer consumes it.

## Safety and maintenance

- Common sensitive HTTP headers and query values are excluded or redacted by default, but
  application-specific secrets still require explicit rules.
- `SnapshotUpdateMode.All` can replace reviewed files. Never enable it in ordinary CI.
- CI updates require a separate explicit authorization.
- Bulk acceptance and deletion require `confirmed: true`; preview paths first.
- Obsolete-file detection is valid only after the complete intended test scope passes.

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting-snapshots-core.html)
- [Snapshot documentation hub](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots.md)
- [Create and review a first snapshot](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/getting-started.md)
- [Snapshot recipes](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/recipes.md)
- [Stabilize and redact data](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/stabilizing-data.md)
- [Maintenance and CI safety](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/maintenance.md)
- [Package migration](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/migration.md)
- [Outbound HTTP snapshot adapters](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Snapshots.Http/README.md)
- [Verify.Xunit adapter](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Verify.Xunit/README.md)
