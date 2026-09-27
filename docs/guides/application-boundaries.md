# Test application boundaries

Integration tests are most useful when they keep the application's real workflow but replace the
slow or nondeterministic edge. Choose a focused guide for the boundary being controlled:

- [Outbound HTTP](outbound-http.md): exact request matching, response sequences, faults,
  recording, verification, and controller-to-database workflows.
- [Published messages](messaging.md): Kafka, Azure Service Bus, Notification Hubs, custom
  transports, typed payload assertions, and failure diagnostics.
- [Observability](observability.md): structured logs, activities, metrics, deterministic time, and
  scenario failure capture.
- [Azure SDK clients](azure-sdk.md): responses, paging, credentials, DI replacement, real SDK
  pipelines, retries, and request verification.
- [Entity Framework Core](entity-framework-core.md): provider choice, database actions, lifecycle,
  isolation, and cleanup.

## Combine boundaries in one scenario

A realistic workflow may read an outbound HTTP response, write database state, publish an event,
and emit telemetry. Build all replacements into one factory and register stateful test doubles as
scenario resources. Each test should then:

1. Arrange only the remote responses needed by that scenario.
2. Create a fresh `TestScenarioScope`.
3. Call the application through its public HTTP endpoint.
4. Assert the response and durable state first.
5. Verify outbound calls, messages, logs, activities, and metrics that are part of the contract.
6. Dispose the scope so resources reset and diagnostics are captured on failure.

Keep assertions at the application boundary. For example, assert the logical message destination
and payload in a controller test, then use a smaller adapter or container test for broker-specific
serialization. Likewise, use an HTTP stub for a typed-client workflow and a separate live-service
test only for behavior that cannot be represented by the remote contract.

Scenario isolation and diagnostic collection are explained in
[scenarios and isolation](../concepts/scenarios-and-isolation.md) and
[resources and cleanup](../concepts/resources-and-cleanup.md). Authentication setup is covered in
the [authentication concept](../concepts/authentication.md).

Browse the [package API index](../api/index.md) for the entry points used at each boundary.
