---
name: easytesting-application-boundaries
description: >-
  Test persistence and external effects of an application using XBullet.EasyTesting EF Core,
  outbound HTTP, messaging, observability, or dependency adapters. Use for integration-test
  arrangements and effect assertions, including background work and boundary failures.
---

# XBullet application boundaries

## Choose what the test must prove

Inspect the application's existing abstractions, dependency registrations, test factory, installed
packages, and current scenario resources. Keep the real application workflow; replace only the
external boundary needed for a deterministic test. Install only packages justified by the
contract and keep production code independent of test helpers.

## Arrange and inspect the boundary

Read [persistence and external effects](references/persistence-and-effects.md) for HTTP, database,
message, telemetry, and asynchronous-work patterns. Select the actual boundary rather than
adding every recorder to every test.

- Database: use `XBullet.EasyTesting.EntityFrameworkCore` with scoped database actions and an
  appropriate provider. In-memory does not prove relational semantics; SQLite does not reproduce
  production-provider SQL. Use the production provider for provider-specific contracts.
- HTTP: use `StubHttpMessageHandler` from `XBullet.EasyTesting.Http` in the application's existing
  HttpClient pipeline. Match method, route, and meaningful headers/body. Verify calls and counts.
- Messaging: adapt the application's publisher to `RecordedMessageBus` and assert route, payload,
  headers, and relevant ordering. This proves logical publication, not real broker delivery.
- Observability: use scenario-scoped collectors and deterministic time when telemetry or time
  affects the contract. Assert structured signals, not incidental formatted logs.

Shared mutable doubles must participate in scenario cleanup. A per-test scope should cover setup,
requests, asynchronous completion, assertions, and disposal. Avoid mutating shared expectations
outside that lifetime.

## Specialized dependencies

Read [specialized hosts and dependencies](references/specialized-dependencies.md) only when the
requirement involves Azure SDK behavior, containers, Aspire, or isolated Functions. Verify the
installed package's documented capabilities before claiming a test models a live service or a
distributed runtime. Missing runtime prerequisites should produce an honest validation limitation,
not a passing test with the meaningful assertion removed.

## Verify observable outcomes

Assert the HTTP result and durable state, then relevant remote calls, published messages, or
telemetry. For background work, wait with a bounded deadline or completion signal; absence checks
require knowing the producer finished. Keep writes and publication outside retrying assertions.
Exercise requested failure paths, including duplicate effects or retry counts when these belong to
the contract. Do not introduce retry behavior into production solely to simplify a test.

Run the affected tests, check isolation by repeated or parallel execution when shared state changed,
and report prerequisites such as Docker or a test database. Do not infer live integration coverage
from a stubbed boundary.
