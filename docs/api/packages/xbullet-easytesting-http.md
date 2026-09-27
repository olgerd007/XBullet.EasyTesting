---
uid: api-package-xbullet-easytesting-http
title: XBullet.EasyTesting.Http API
---

# XBullet.EasyTesting.Http

Deterministic outbound HTTP responses, failures, sequences, call recording, and verification.

```xml
<PackageReference Include="XBullet.EasyTesting.Http" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.Http.StubHttpMessageHandler> matches requests and returns configured responses.
- <xref:XBullet.EasyTesting.Http.StubHttpResponseBuilder> creates response rules.
- <xref:XBullet.EasyTesting.Http.StubHttpExchange> records completed calls and failures.

See the [outbound HTTP guide](../../guides/outbound-http.md).
