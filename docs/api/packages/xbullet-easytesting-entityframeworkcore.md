---
uid: api-package-xbullet-easytesting-entityframeworkcore
title: XBullet.EasyTesting.EntityFrameworkCore API
---

# XBullet.EasyTesting.EntityFrameworkCore

Database-aware application factories and per-scenario Entity Framework Core setup.

```xml
<PackageReference Include="XBullet.EasyTesting.EntityFrameworkCore" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.EntityFrameworkCore.EntityFrameworkWebApplicationFactory`2> defines database-backed hosts.
- <xref:XBullet.EasyTesting.EntityFrameworkCore.InMemoryEntityFrameworkWebApplicationFactory`2>
  provides an in-memory host.
- <xref:XBullet.EasyTesting.EntityFrameworkCore.DatabaseScenarioBuilder`2> configures scenario data and cleanup.

See the [Entity Framework Core guide](../../guides/entity-framework-core.md).
