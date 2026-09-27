# Stabilize dynamic and sensitive snapshot data

Transform only values that are nondeterministic, sensitive, or irrelevant to the contract. A
scrubber that hides meaningful behavior makes a snapshot pass for the wrong reason.

## Member rules

- `ScrubMember` or `ScrubMembers` keeps the property and writes `{Scrubbed}`.
- `IgnoreMember` or `IgnoreMembers` removes the property.
- `ScrubGuids` replaces JSON string GUIDs with `{Guid}`.
- `ScrubDateTimes` replaces round-trip date/time strings with `{DateTime}`.
- `Scrub` applies a final custom string transformation.

Member matching is case-insensitive and recursive. This executable before/after test proves that
different IDs and timestamps match while a secret property never reaches the verified file:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-structured-scrubbing -->
```csharp
[Fact]
public async Task Structured_scrubbing_stabilizes_dynamic_values_and_ignored_members()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = new SnapshotSettings()
        .InDirectory(snapshotDirectory)
        .Named("structured-scrubbing")
        .ScrubMembers("RequestId", "CreatedAt")
        .IgnoreMembers("AccessToken")
        .ScrubGuids()
        .ScrubDateTimes()
        .Updating(SnapshotUpdateMode.Missing)
        .AllowingUpdatesInContinuousIntegration()
        .WithoutDiffTool();

    try
    {
        await SnapshotAssert.MatchAsync(
            new
            {
                RequestId = 123,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
                AccessToken = "secret-one",
                Metadata = new
                {
                    CorrelationId = Guid.Parse("3c91271a-a927-4516-b225-41cce45963b5"),
                    ExpiresAt = "2026-01-01T11:00:00Z"
                }
            },
            settings,
            cancellationToken);

        settings.Updating(SnapshotUpdateMode.None);
        await SnapshotAssert.MatchAsync(
            new
            {
                RequestId = 999,
                CreatedAt = new DateTimeOffset(2030, 2, 2, 12, 0, 0, TimeSpan.Zero),
                AccessToken = "secret-two",
                Metadata = new
                {
                    CorrelationId = Guid.Parse("637717e4-10d3-4302-bff2-846b17f00591"),
                    ExpiresAt = "2030-02-02T13:00:00Z"
                }
            },
            settings,
            cancellationToken);

        var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
        var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);

        Assert.Contains("\"RequestId\": \"{Scrubbed}\"", verified);
        Assert.Contains("\"CorrelationId\": \"{Guid}\"", verified);
        Assert.Contains("\"ExpiresAt\": \"{DateTime}\"", verified);
        Assert.DoesNotContain("AccessToken", verified);
        Assert.DoesNotContain("secret-one", verified);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Conceptually, this input:

```json
{"RequestId":123,"AccessToken":"secret","Metadata":{"CorrelationId":"3c91271a-a927-4516-b225-41cce45963b5"}}
```

becomes:

```json
{"RequestId":"{Scrubbed}","Metadata":{"CorrelationId":"{Guid}"}}
```

## Path-specific rules

Use extended JSON Pointer rules when a member name elsewhere must remain visible:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-path-rules -->
```csharp
[Fact]
public async Task Json_pointer_rules_target_nested_and_wildcard_values()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = CreateUpdatingSettings(snapshotDirectory)
        .Named("pointer-rules")
        .ScrubPath("/orders/*/id")
        .IgnorePath("/orders/*/generatedAt")
        .ReplacePath("/status", "stable")
        .ReplacePath("/escaped~1name/~2", "literal-star");

    try
    {
        await SnapshotAssert.MatchJsonAsync(
            """
            {
              "status": "created",
              "orders": [
                { "id": 101, "name": "Keyboard", "generatedAt": "2026-01-01T10:00:00Z" },
                { "id": 102, "name": "Mouse", "generatedAt": "2026-01-01T10:01:00Z" }
              ],
              "escaped/name": { "*": "dynamic" }
            }
            """,
            settings,
            cancellationToken);

        settings.Updating(SnapshotUpdateMode.None);
        await SnapshotAssert.MatchJsonAsync(
            """
            {
              "status": "completed",
              "orders": [
                { "id": 901, "name": "Keyboard", "generatedAt": "2030-02-02T12:00:00Z" },
                { "id": 902, "name": "Mouse", "generatedAt": "2030-02-02T12:01:00Z" }
              ],
              "escaped/name": { "*": "changed" }
            }
            """,
            settings,
            cancellationToken);

        var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
        Assert.Equal(2, CountOccurrences(verified, "{Scrubbed}"));
        Assert.Contains("\"status\": \"stable\"", verified);
        Assert.Contains("\"*\": \"literal-star\"", verified);
        Assert.DoesNotContain("generatedAt", verified);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Available rules include `ScrubPath`, `IgnorePath`, `ReplacePath`, `HashPath`, and `SortArray`.
Paths are case-sensitive. `*` selects one object or array level; escape `/` as `~1`, `~` as `~0`,
and a literal wildcard property as `~2`.

## Hash large or sensitive subtrees

`HashPath` replaces a selected value with a SHA-256 marker computed from canonical JSON. Equivalent
objects hash identically even when property order differs:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-hashing -->
```csharp
[Fact]
public async Task Hashed_paths_use_canonical_json_content()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = CreateUpdatingSettings(snapshotDirectory)
        .Named("hashed-path")
        .HashPath("/payload");

    try
    {
        await SnapshotAssert.MatchJsonAsync(
            """{"name":"document","payload":{"b":2,"a":1}}""",
            settings,
            cancellationToken);

        settings.Updating(SnapshotUpdateMode.None);
        await SnapshotAssert.MatchJsonAsync(
            """{"name":"document","payload":{"a":1,"b":2}}""",
            settings,
            cancellationToken);

        var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
        Assert.Contains("\"payload\": \"sha256:", verified);
        Assert.DoesNotContain("\"a\": 1", verified);
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Hashing hides content but still reveals whether it changed. Do not treat an unsalted hash of a
small secret domain as cryptographic protection; omit or redact credentials instead.

## Canonical properties and sorted arrays

Use `CanonicalizeJson` when object property order is incidental. Use `SortArray(path, keyPath)` only
when array order is not part of behavior:

<!-- snippet: tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs#docs-snapshots-canonical-order -->
```csharp
[Fact]
public async Task Canonical_properties_and_sorted_arrays_ignore_incidental_order()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var snapshotDirectory = CreateTemporarySnapshotDirectory();
    var settings = CreateUpdatingSettings(snapshotDirectory)
        .Named("canonical-order")
        .SortArray("/items", "/id")
        .CanonicalizeJson();

    try
    {
        await SnapshotAssert.MatchJsonAsync(
            """{"z":1,"items":[{"name":"B","id":"2"},{"name":"A","id":"1"}],"a":2}""",
            settings,
            cancellationToken);

        settings.Updating(SnapshotUpdateMode.None);
        await SnapshotAssert.MatchJsonAsync(
            """{"a":2,"items":[{"id":"1","name":"A"},{"id":"2","name":"B"}],"z":1}""",
            settings,
            cancellationToken);

        var verified = await ReadSingleVerifiedSnapshotAsync(snapshotDirectory, cancellationToken);
        Assert.True(
            verified.IndexOf("\"a\"", StringComparison.Ordinal) <
            verified.IndexOf("\"items\"", StringComparison.Ordinal));
        Assert.True(
            verified.IndexOf("\"id\": \"1\"", StringComparison.Ordinal) <
            verified.IndexOf("\"id\": \"2\"", StringComparison.Ordinal));
    }
    finally
    {
        DeleteTemporarySnapshotDirectory(snapshotDirectory);
    }
}
```
<!-- end-snippet -->

Do not sort event streams, priority lists, or API results whose order is contractual.

## HTTP redaction and URL stability

Controller, TestServer exchange, and outbound-stub options support:

- Ignoring a header or query parameter entirely.
- Redacting its value as `{Redacted}` while retaining its presence.
- Explicitly including a normally excluded value when it is safe and stable.
- Scrubbing GUID route segments and selected query values as `{Scrubbed}`.
- Replacing custom route values with `ScrubbingUrlPath`.

Common secret query names—including `access_token`, `api_key`, `client_secret`, `sas`, `secret`,
`sig`, and `token`—are redacted by default. If a name is both scrubbed and redacted, security
redaction wins. Application-specific sensitive names still require explicit configuration.

Review the [complete-exchange recipe](recipes.md#sensitive-complete-exchange) for a realistic nested
body and request-header example.

See the [snapshot core API reference](../../api/packages/xbullet-easytesting-snapshots-core.md).
