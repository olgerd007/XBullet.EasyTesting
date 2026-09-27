---
uid: api-package-xbullet-easytesting
title: XBullet.EasyTesting API
---

# XBullet.EasyTesting

Core test hosts, isolated scenarios, authentication helpers, and HTTP response assertions.

```xml
<PackageReference Include="XBullet.EasyTesting" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.Hosting.EasyTestHost> creates minimal-hosting test applications.
- <xref:XBullet.EasyTesting.Hosting.TestScenarioScope`1> owns one isolated scenario and its resources.
- <xref:XBullet.EasyTesting.Authentication.TestJwtBuilder> creates deterministic JWT bearer tokens.
- <xref:XBullet.EasyTesting.Hosting.TestHttpResponseAssertions> provides fluent response assertions.

## Related guides

- [First controller test](../../getting-started/first-controller-test.md)
- [Test hosts](../../concepts/test-hosts.md)
- [Authentication and scenarios](../../guides/authentication-and-scenarios.md)
- [Response assertions](../../guides/response-assertions.md)
