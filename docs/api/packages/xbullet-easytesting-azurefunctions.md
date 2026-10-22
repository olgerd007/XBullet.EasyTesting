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

- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestFactory`1> reuses an executable application's
  startup configuration and DI container with test overrides.
- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestHost> resolves and invokes functions.
- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestHostBuilder> configures services and middleware.
- <xref:XBullet.EasyTesting.AzureFunctions.AzureFunctionTestScenarioScope> owns resource resets and scenario cleanup.
- <xref:XBullet.EasyTesting.AzureFunctions.FunctionScenario> provides guarded, single-use domain arrangement.
- <xref:XBullet.EasyTesting.AzureFunctions.FunctionScopedTest> provides a borrowed host and isolated scenario runner.
- <xref:XBullet.EasyTesting.AzureFunctions.TestHttpRequestBuilder> creates HTTP trigger requests.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableTaskClient> records client attempts, seeds
  metadata fixtures, provides management handlers, and participates in scenario diagnostics/reset.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableWorkflow> connects named orchestrators,
  activities, children, and external events to a client through a borrowed function host's DI scopes
  and middleware, with metadata, result waits, diagnostics, and bounded cleanup. `WithSerialization`
  optionally checks payload conversion through the SDK JSON converter or a supplied `DataConverter`.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableTaskSequence`1> supplies ordered results and
  failures with explicit exhaustion and scenario reset.
- <xref:XBullet.EasyTesting.AzureFunctions.RecordedDurableTaskAttempt> records actual activity/child
  attempts. `TestDurableWorkflow.WithRetries` enables bounded SDK policy/handler simulation using manual time.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableTaskSequenceExhaustedException> identifies
  exhausted test scripts and stops retry simulation.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableEntityClient> provides entity signals,
  typed metadata fixtures, query/cleanup handlers, and scenario diagnostics/reset through `client.Entities`.
- <xref:XBullet.EasyTesting.AzureFunctions.TestOrchestrationEntityFeature> records orchestrator
  calls, signals, and local lock markers with explicitly configured results.
- <xref:XBullet.EasyTesting.AzureFunctions.TestEntityOperation>,
  <xref:XBullet.EasyTesting.AzureFunctions.TestEntityContext>, and
  <xref:XBullet.EasyTesting.AzureFunctions.TestEntityState> run real SDK entity implementations
  with serialized input/state and recorded outgoing operations.
- <xref:XBullet.EasyTesting.AzureFunctions.RecordedEntityOperation> and
  <xref:XBullet.EasyTesting.AzureFunctions.EntityOperationKind> describe entity interaction attempts.
- <xref:XBullet.EasyTesting.AzureFunctions.RecordedOrchestrationStart> exposes scheduling input,
  options, task name, and selected instance ID.
- <xref:XBullet.EasyTesting.AzureFunctions.RecordedDurableClientOperation> exposes invocation order,
  operation kind, target ID, and original SDK arguments.
- <xref:XBullet.EasyTesting.AzureFunctions.DurableClientOperation> identifies client operation kinds.
- <xref:XBullet.EasyTesting.AzureFunctions.TestOrchestrationMetadataBuilder> creates SDK instance
  metadata with typed payloads, failures, tags, and deterministic timestamps.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableTaskClientAssertions> verifies counts, targets,
  arguments, and operation sequences with readable failure timelines.
- <xref:XBullet.EasyTesting.AzureFunctions.TestDurableTaskClientVerificationException> reports a
  mismatched client assertion.
- <xref:XBullet.EasyTesting.AzureFunctions.TestOrchestrationContext> provides typed input, identity,
  replay flags, manual timers, buffered events, recorded dispatch, and configurable runtime stubs.

See the [host guide](../../guides/azure-functions.md), [trigger recipes](../../guides/azure-functions-triggers.md),
and [Durable boundary](../../guides/azure-functions-durable.md). See
[connected workflows](../../guides/azure-functions-durable-workflows.md) for end-to-end dispatch.
See [Durable Entity tests](../../guides/azure-functions-durable-entities.md) for entity boundaries.
