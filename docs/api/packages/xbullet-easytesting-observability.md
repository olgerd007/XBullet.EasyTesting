---
uid: api-package-xbullet-easytesting-observability
title: XBullet.EasyTesting.Observability API
---

# XBullet.EasyTesting.Observability

Structured log, activity, and metric collectors with deterministic test time.

```xml
<PackageReference Include="XBullet.EasyTesting.Observability" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.Observability.TestObservability> coordinates collectors and time.
- <xref:XBullet.EasyTesting.Observability.TestLogCollector> captures structured logs.
- <xref:XBullet.EasyTesting.Observability.TestActivityCollector> captures distributed tracing activities.
- <xref:XBullet.EasyTesting.Observability.TestMetricCollector> captures measurements.

See the [observability guide](../../guides/observability.md).
