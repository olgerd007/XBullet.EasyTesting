---
uid: api-package-xbullet-easytesting-snapshots-core
title: XBullet.EasyTesting.Snapshots.Core API
---

# XBullet.EasyTesting.Snapshots.Core

Framework-independent JSON, text, controller, and complete HTTP exchange snapshot assertions.

```xml
<PackageReference Include="XBullet.EasyTesting.Snapshots.Core" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.Snapshots.SnapshotAssert> verifies JSON and text snapshots.
- <xref:XBullet.EasyTesting.Snapshots.SnapshotAssertionExtensions> provides fluent assertions for
  values and named variants.
- <xref:XBullet.EasyTesting.Snapshots.SnapshotSettings> controls naming, updates, and transformations.
- <xref:XBullet.EasyTesting.Snapshots.HttpResponseSnapshotExtensions> captures controller responses and exchanges.
- <xref:XBullet.EasyTesting.Snapshots.SnapshotMaintenance> performs guarded bulk maintenance.

Start with the [snapshot workflow](../../guides/snapshots.md).
See [stable snapshot data](../../guides/snapshots/stabilizing-data.md) for opt-in JSON string
line-ending normalization, redaction, and deterministic transformations.
