---
uid: api-reference
title: API reference
description: Searchable public API reference for every XBullet.EasyTesting package.
---

# API reference

The reference is generated from the public Release build and its XML comments. Start with a
package page to understand its purpose and primary entry points, or browse the generated namespace
index for every public type and member.

## Packages

| Package | Purpose |
| --- | --- |
| [XBullet.EasyTesting](packages/xbullet-easytesting.md) | Test hosts, scenarios, authentication, and response assertions |
| [XBullet.EasyTesting.Aspire](packages/xbullet-easytesting-aspire.md) | Aspire distributed-application tests |
| [XBullet.EasyTesting.Azure](packages/xbullet-easytesting-azure.md) | Azure SDK credentials, responses, and pipeline transport |
| [XBullet.EasyTesting.AzureFunctions](packages/xbullet-easytesting-azurefunctions.md) | Isolated-worker function invocation and trigger data |
| [XBullet.EasyTesting.EntityFrameworkCore](packages/xbullet-easytesting-entityframeworkcore.md) | Database-backed test hosts and scenario setup |
| [XBullet.EasyTesting.Http](packages/xbullet-easytesting-http.md) | Deterministic outbound HTTP stubs |
| [XBullet.EasyTesting.Messaging](packages/xbullet-easytesting-messaging.md) | Recorded-message test doubles and assertions |
| [XBullet.EasyTesting.Observability](packages/xbullet-easytesting-observability.md) | Logs, activities, metrics, and deterministic time |
| [XBullet.EasyTesting.Snapshots.Core](packages/xbullet-easytesting-snapshots-core.md) | Framework-independent snapshot assertions |
| [XBullet.EasyTesting.Snapshots.Http](packages/xbullet-easytesting-snapshots-http.md) | Snapshot adapters for outbound HTTP stubs |
| [XBullet.EasyTesting.Snapshots](packages/xbullet-easytesting-snapshots.md) | Compatibility facade for snapshot packages |
| [XBullet.EasyTesting.Testcontainers](packages/xbullet-easytesting-testcontainers.md) | Container-backed scenario resources |
| [XBullet.EasyTesting.Verify.Xunit](packages/xbullet-easytesting-verify-xunit.md) | Verify.Xunit adapters for controller snapshots |

## Reference policy

The site documents APIs from the `net10.0` build because all shipped implementation packages
multi-target .NET 8, .NET 9, and .NET 10 with the same checked public surface. The
`XBullet.EasyTesting.Snapshots` compatibility package only forwards types from
`XBullet.EasyTesting.Snapshots.Core` and `XBullet.EasyTesting.Snapshots.Http`; its package page
documents that supported facade instead of generating duplicate type pages.

See [building the reference](../contributing/api-reference.md) for local commands, validation, and
release behavior.
