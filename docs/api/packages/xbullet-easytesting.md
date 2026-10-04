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
- <xref:XBullet.EasyTesting.Hosting.EasyTestHostBuilder`1> composes host overrides, including
  `UseEnvironment` to change the default `Testing` environment before startup.
- <xref:XBullet.EasyTesting.Hosting.TestScenarioScope`1> owns one isolated scenario and its resources.
- <xref:XBullet.EasyTesting.Hosting.Scenario`1> provides reusable domain arrangement with single-use guards.
- <xref:XBullet.EasyTesting.Hosting.ScopedTest`2> provides a concrete factory and isolated test runner.
- <xref:XBullet.EasyTesting.Authentication.TestJwtBuilder> creates deterministic JWT bearer tokens.
- <xref:XBullet.EasyTesting.Hosting.TestHttpResponseAssertions> provides fluent response assertions.
- <xref:XBullet.EasyTesting.Eventually> retries assertions or polls asynchronous conditions.
- <xref:XBullet.EasyTesting.EventuallyOptions> configures deadlines, polling, and deterministic time.
- <xref:XBullet.EasyTesting.EventuallyTimeoutException> retains the last assertion failure and attempt diagnostics.

## Related guides

- [First controller test](../../getting-started/first-controller-test.md)
- [Test hosts](../../concepts/test-hosts.md)
- [Domain scenarios and scoped tests](../../concepts/scenarios-and-isolation.md#reusable-domain-scenarios)
- [Authentication and scenarios](../../guides/authentication-and-scenarios.md)
- [Response assertions](../../guides/response-assertions.md)
- [Wait for background work](../../guides/eventual-assertions.md)
