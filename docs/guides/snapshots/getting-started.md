# Create and review a first snapshot

Install the built-in engine in the test project:

```shell
dotnet add package XBullet.EasyTesting.Snapshots.Core
```

Import `XBullet.EasyTesting.Snapshots`. Snapshot an arbitrary serializable value with
`SnapshotAssert.MatchAsync`, raw JSON with `MatchJsonAsync`, or an HTTP controller response with
`ShouldMatchControllerSnapshot`.

## Snapshot a controller response

This executable example calls an authenticated controller and records its request method, relative
URL, numeric status, reason phrase, stable headers, and body:

<!-- snippet: tests/TestApi.IntegrationTests/BuiltInSnapshotTests.cs#docs-snapshots-built-in-controller -->
```csharp
[Fact]
public Task Authenticated_controller_matches_own_snapshot() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = scope.CreateAuthenticatedClient(
            TestUser.Create(name: "Grace", nameIdentifier: "user-84"));
        using var response = await client.GetAsync("/api/secure/me", cancellationToken);

        await response.ShouldMatchControllerSnapshot(
            snapshotSettings: BuiltInSnapshotAudit.CreateSettings(),
            cancellationToken: cancellationToken);
    });
```
<!-- end-snippet -->

JSON bodies are structural. Text remains text, empty content becomes `null`, and binary content is
stored as base64. Volatile and credential-bearing headers are excluded by default.

## Review the first failure

With no verified file, the assertion writes a runtime-qualified received file and throws
`SnapshotMismatchException`. Review it, promote it, and run the test again:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-first-acceptance -->
```csharp
[Fact]
public async Task New_snapshot_writes_a_received_file()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = new SnapshotSettings()
        .InDirectory(snapshotDirectory)
        .Named("new-snapshot")
        .Updating(SnapshotUpdateMode.None)
        .WithoutDiffTool();

    try
    {
        var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
            SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken));

        Assert.True(File.Exists(exception.ReceivedPath));
        Assert.False(File.Exists(exception.VerifiedPath));
        Assert.Matches(@"\.received\.net(8|9|10)\.0\.json$", exception.ReceivedPath);

        var verifiedPath = SnapshotAssert.AcceptReceived(exception.ReceivedPath);

        Assert.Equal(exception.VerifiedPath, verifiedPath);
        Assert.True(File.Exists(verifiedPath));
        await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

For an ordinary test, the files look like:

```text
__snapshots__/
  OrderTests.Get_order.received.net10.0.json
  OrderTests.Get_order.verified.json
```

Commit the `*.verified.*` file. Do not commit `*.received.*` files. The verified file is shared by
all target frameworks; the received name includes the target framework so concurrent .NET 8, 9,
and 10 runs cannot overwrite one another.

## Safe review checklist

Before accepting:

1. Confirm every changed value represents intended behavior.
2. Remove, ignore, hash, or redact nondeterministic and sensitive fields.
3. Ensure the snapshot is small enough to review as a contract.
4. Rerun the test with updates disabled.
5. Commit the verified file together with the behavior change.

Use explicit assertions for a few scalar rules. Prefer a snapshot when the value is cohesive and a
reviewer benefits from seeing the complete shape.

Next: [file locations and naming](files-and-naming.md), [snapshot recipes](recipes.md), and
[stabilizing data](stabilizing-data.md).

See the [snapshot core API reference](../../api/packages/xbullet-easytesting-snapshots-core.md).
