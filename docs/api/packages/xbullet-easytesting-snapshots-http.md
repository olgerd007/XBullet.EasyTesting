---
uid: api-package-xbullet-easytesting-snapshots-http
title: XBullet.EasyTesting.Snapshots.Http API
---

# XBullet.EasyTesting.Snapshots.Http

Snapshot adapters for requests and exchanges captured by `XBullet.EasyTesting.Http`.

```xml
<PackageReference Include="XBullet.EasyTesting.Snapshots.Http" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.Snapshots.StubHttpRequestSnapshotExtensions> verifies captured requests.
- <xref:XBullet.EasyTesting.Snapshots.StubHttpExchangeSnapshotExtensions> verifies complete stub exchanges.
- <xref:XBullet.EasyTesting.Snapshots.StubHttpRequestSnapshotOptions> controls request redaction and content.

See the [outbound snapshot recipes](../../guides/snapshots/recipes.md#outbound-http-stub-requests-and-exchanges).
