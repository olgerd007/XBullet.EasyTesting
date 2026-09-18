# XBullet.EasyTesting.Snapshots

Compatibility facade for the snapshot package split. It references and forwards all existing
public types to:

- `XBullet.EasyTesting.Snapshots.Core` for framework-independent JSON and HTTP-response snapshots.
- `XBullet.EasyTesting.Snapshots.Http` for `XBullet.EasyTesting.Http` request-capture adapters.

Existing package references and namespaces continue to work. New projects that do not use
outbound HTTP stubs should reference `XBullet.EasyTesting.Snapshots.Core` directly to avoid the
ASP.NET testing dependency graph.
