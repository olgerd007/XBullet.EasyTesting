# Choose built-in snapshots or Verify.Xunit

Both integrations can capture normalized controller responses and complete TestServer exchanges.
Choose based on the approval workflow already used by the test suite.

| Consideration | Built-in engine | Verify.Xunit adapter |
| --- | --- | --- |
| Package | `XBullet.EasyTesting.Snapshots.Core` | `XBullet.EasyTesting.Verify.Xunit` |
| Test framework dependency | None | xUnit v3 and Verify.Xunit |
| Arbitrary values, raw JSON, and plain text | Built in | Use Verify directly for additional shapes |
| Controller and complete exchange model | Supported | Same normalized XBullet capture model converted for Verify |
| File/update workflow | XBullet received, verified, maintenance, and CI guards | Verify's naming, diffing, scrubbers, and acceptance workflow |
| Outbound `StubHttpMessageHandler` snapshots | `XBullet.EasyTesting.Snapshots.Http` | Not provided by this adapter |
| Project defaults | XBullet global and fixture-local templates | Verify settings and Verify project configuration |

## Prefer the built-in engine when

- The project should not depend on a test framework or Verify.
- One workflow should cover JSON, text, controller responses, real exchanges, and outbound stubs.
- Runtime-qualified received files and guarded bulk maintenance match the team's CI model.
- XBullet's structural path transformations and defaults are sufficient.

## Prefer Verify.Xunit when

- The xUnit v3 suite already uses Verify and contributors know its approval workflow.
- Existing Verify scrubbers, converters, extensions, or tooling should own snapshot review.
- The required XBullet surface is controller responses or complete TestServer exchanges.

The adapter is invoked directly on the real controller response:

<!-- snippet: tests/TestApi.IntegrationTests/ControllerSnapshotTests.cs#docs-snapshots-verify-controller -->
```csharp
[Fact]
public Task Authenticated_controller_matches_snapshot() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = scope.CreateAuthenticatedClient(
            TestUser.Create(name: "Ada", nameIdentifier: "user-42"));
        using var response = await client.GetAsync("/api/secure/me", cancellationToken);

        await response.VerifyControllerSnapshot(cancellationToken: cancellationToken);
    });
```
<!-- end-snippet -->

## Coexistence

Both engines can coexist in one test project. Assign one owner per snapshot type and document the
choice so contributors know which received files, acceptance commands, and configuration apply.
Do not run the same contract through both engines unless comparing adapters is itself the test.

For Verify-specific controller and exchange configuration, continue with the
[Verify.Xunit guide](../verify-xunit.md). For the built-in approval workflow, see
[getting started](getting-started.md) and [maintenance](maintenance.md).

API references: [built-in engine](../../api/packages/xbullet-easytesting-snapshots-core.md) and
[Verify.Xunit adapter](../../api/packages/xbullet-easytesting-verify-xunit.md).
