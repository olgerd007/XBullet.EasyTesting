---
uid: api-package-xbullet-easytesting-snapshots
title: XBullet.EasyTesting.Snapshots compatibility API
---

# XBullet.EasyTesting.Snapshots

Compatibility facade for applications that installed the original combined snapshot package.

```xml
<PackageReference Include="XBullet.EasyTesting.Snapshots" Version="VERSION" />
```

This package forwards its public types to
[XBullet.EasyTesting.Snapshots.Core](xbullet-easytesting-snapshots-core.md) and
[XBullet.EasyTesting.Snapshots.Http](xbullet-easytesting-snapshots-http.md). It has no separate API
pages because doing so would duplicate the same supported types and identifiers.

New installations should select the smallest implementation package that provides the required
workflow. Existing applications may retain the facade during migration. See the
[migration guide](../../guides/snapshots/migration.md).
