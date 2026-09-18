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

### Raw JSON

Raw JSON can be verified as structured JSON instead of as an escaped string. Raw strings and
HTTP content are parsed and normalized with `System.Text.Json`, and snapshots use the
`*.verified.json` and `*.received.json` file extensions.

```csharp
var json = $$"""
    {
      "orderId": 42,
      "status": "ready",
      "correlationId": "{{Guid.NewGuid()}}"
    }
    """;
var settings = new SnapshotSettings()
    .ScrubGuids();

await SnapshotAssert.MatchJsonAsync(json, settings);
```

The resulting verified snapshot contains normalized JSON:

```json
{
  "orderId": 42,
  "status": "ready",
  "correlationId": "{Guid}"
}
```

Invalid JSON throws `JsonException` without creating a snapshot.

### HTTP JSON content

Verify only the JSON response body when status, headers, and request metadata do not belong in the
snapshot:

```csharp
using var response = await client.GetAsync("/api/orders/42");
response.EnsureSuccessStatusCode();

await response.Content.ShouldMatchJsonSnapshot();
```

Use `response.ShouldMatchControllerSnapshot()` instead when the snapshot should also contain the
request method and URL, response status, and stable headers.

The first run writes a received snapshot. Review and approve it as the verified snapshot; subsequent runs report structural differences. Update modes and local diff viewers are opt-in.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for controller snapshots, scrubbers, and approval workflows.
