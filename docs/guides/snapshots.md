# Snapshot testing

Snapshot tests compare a stable representation of current behavior with a reviewed file committed
to source control. Use them when a response or structured result contains enough fields that
individual assertions would obscure the contract.

## Choose a package

| Goal | Install | Notes |
| --- | --- | --- |
| Snapshot values, JSON, text, controller responses, or complete TestServer exchanges | `XBullet.EasyTesting.Snapshots.Core` | Framework-independent built-in engine; no dependency on the outbound HTTP stub package |
| Snapshot requests or exchanges recorded by `StubHttpMessageHandler` | `XBullet.EasyTesting.Snapshots.Http` | Adds adapters for `XBullet.EasyTesting.Http`; also references snapshot core |
| Keep an existing reference to the former combined package | `XBullet.EasyTesting.Snapshots` | Compatibility facade forwarding core and HTTP types; prefer specific packages for new projects |
| Use the Verify.Xunit v3 approval workflow for controller or TestServer exchange snapshots | `XBullet.EasyTesting.Verify.Xunit` | Optional xUnit-specific adapter using Verify's files and configuration |

You do not need to understand the package history to start: install snapshot core unless the test
snapshots an outbound HTTP stub or intentionally uses Verify.Xunit.

## Follow the workflow

1. [Create and review a first snapshot](snapshots/getting-started.md).
2. Understand [file locations, names, variants, and parallel target runs](snapshots/files-and-naming.md).
3. Choose a [recipe for JSON, text, controller responses, exchanges, or outbound HTTP](snapshots/recipes.md).
4. [Stabilize dynamic and sensitive data](snapshots/stabilizing-data.md).
5. Configure [project-wide or fixture-local defaults](snapshots/defaults.md).
6. Use the guarded [mismatch, acceptance, update, and cleanup workflow](snapshots/maintenance.md).

Existing users can follow the [package-split migration guide](snapshots/migration.md). Teams deciding
between approval engines should read [built-in snapshots versus Verify.Xunit](snapshots/choose-engine.md).

## Core rule

A verified snapshot is reviewed test code. Never accept or bulk-update it merely to make a build
green. Inspect the difference, confirm that sensitive data is absent, and understand the behavior
change before committing the new verified file.

Browse the [snapshot core API reference](../api/packages/xbullet-easytesting-snapshots-core.md),
[HTTP adapter API reference](../api/packages/xbullet-easytesting-snapshots-http.md), or
[Verify.Xunit API reference](../api/packages/xbullet-easytesting-verify-xunit.md).
