# XBullet.EasyTesting.Snapshots

Framework-independent JSON snapshot assertions for controller responses, outbound HTTP requests, and arbitrary serializable values.

The package targets .NET 8 and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Snapshots
```

```csharp
var settings = new SnapshotSettings()
    .Named("administrator-order")
    .ScrubMembers("Id", "CreatedAt")
    .ScrubGuids();

await SnapshotAssert.MatchAsync(result, settings);
```

The first run writes a received snapshot. Review and approve it as the verified snapshot; subsequent runs report structural differences. Update modes and local diff viewers are opt-in.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for controller snapshots, scrubbers, and approval workflows.
