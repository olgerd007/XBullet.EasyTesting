# XBullet.EasyTesting.Snapshots

Compatibility facade for the snapshot package split. It references and forwards all existing
public types to the two replacement packages.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Snapshots
```

## Package selection

- `XBullet.EasyTesting.Snapshots.Core` for framework-independent JSON and HTTP-response snapshots.
- `XBullet.EasyTesting.Snapshots.Http` for `XBullet.EasyTesting.Http` request-capture adapters.

Existing package references and namespaces continue to work. New projects that do not use
outbound HTTP stubs should reference `XBullet.EasyTesting.Snapshots.Core` directly to avoid the
ASP.NET testing dependency graph.

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting-snapshots.html)
- [Built-in snapshot guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots.md)
- [Package-split migration guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/migration.md)
- [Snapshot core package](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Snapshots/README.md)
- [Outbound HTTP snapshot adapters](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Snapshots.Http/README.md)
