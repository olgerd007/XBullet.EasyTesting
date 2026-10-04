---
uid: api-package-xbullet-easytesting-azurefunctions
title: XBullet.EasyTesting.AzureFunctions API
---

# XBullet.EasyTesting.AzureFunctions

In-process invocation tools for .NET isolated-worker functions and trigger data.

```xml
<PackageReference Include="XBullet.EasyTesting.AzureFunctions" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestHost> resolves and invokes functions.
- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestHostBuilder> configures services and middleware.
- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestScenarioScope> owns resource resets and scenario cleanup.
- <xref:XBullet.EasyTesting.AzureFunctions.FunctionScenario> provides guarded, single-use domain arrangement.
- <xref:XBullet.EasyTesting.AzureFunctions.FunctionScopedTest> provides a borrowed host and isolated scenario runner.
- <xref:XBullet.EasyTesting.AzureFunctions.TestHttpRequestBuilder> creates HTTP trigger requests.
- <xref:XBullet.EasyTesting.AzureFunctions.TestOrchestrationContext> emulates supported activity dispatch.

See the [host guide](../../guides/azure-functions.md), [trigger recipes](../../guides/azure-functions-triggers.md),
and [Durable boundary](../../guides/azure-functions-durable.md).
