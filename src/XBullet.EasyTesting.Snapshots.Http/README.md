# XBullet.EasyTesting.Snapshots.Http

Snapshot adapters for requests captured by `XBullet.EasyTesting.Http`. The package references
`XBullet.EasyTesting.Snapshots.Core` and keeps the existing
`XBullet.EasyTesting.Snapshots` namespace.

```shell
dotnet add package XBullet.EasyTesting.Snapshots.Http
```

```csharp
await handler.ShouldMatchRequestsSnapshot(
    new StubHttpRequestSnapshotOptions()
        .IgnoringHeaders("X-Retry-Count"),
    new SnapshotSettings()
        .ScrubMembers("timestamp", "requestId"));
```

JSON request bodies are captured structurally with `System.Text.Json`. Authorization, cookies,
API keys, correlation IDs, and tracing headers are excluded by default.
