# Design multi-target and parallel tests

All XBullet.EasyTesting packages target .NET 8, .NET 9, and .NET 10. A solution test can therefore
run several target-framework processes at the same time, while the test runner can also schedule
multiple tests inside each process. Design isolation at both levels.

Parallel execution is safe when each concurrent owner has distinct mutable state. Sharing an
immutable factory configuration is different from sharing a database, recorder, collector, file,
port, queue, or container.

## Identify the concurrency boundary

| Boundary | What can run concurrently | Isolation requirement |
| --- | --- | --- |
| Target framework | .NET 8, .NET 9, and .NET 10 test processes | Use framework-qualified transient files and avoid fixed process-external names |
| Test runner | Test classes or cases in one process | Do not share mutable state unless the owning fixture serializes access |
| Scenario scope | Child hosts created from one factory | The factory scenario gate serializes scopes that mutate its registered resources |
| External infrastructure | Databases, brokers, storage, emulators, and containers | Give each concurrent owner a distinct database, schema, namespace, topic, queue, container, or resource instance |
| Generated artifacts | Snapshots, logs, coverage, and test results | Give each logical contract one stable name and each transient writer a collision-safe path |

## Share factories, isolate scenarios

A fixture can own a long-lived factory, but each stateful test should create and asynchronously
dispose its own `TestScenarioScope`. The factory's scenario gate covers the complete scope lifetime,
including resource reset and cleanup. Two scopes from the same factory wait rather than mutating
the same registered recorder or database hooks concurrently.

This protection does not coordinate separate factory instances. If parallel tests construct two
factories that point to the same database or process-wide dependency, those tests must use distinct
resource names or an explicit runner-level collection that serializes them.

See [scenarios and isolation](scenarios-and-isolation.md) for scope behavior and
[resources and cleanup](resources-and-cleanup.md) for ownership rules.

## Isolate mutable dependencies

- Database tests should use a scenario-specific database, schema, file, or connection. Do not
  combine parallel factory instances with one mutable database unless the lifecycle is explicitly
  coordinated.
- HTTP and message recorders should be registered as scenario resources so rules and captured calls
  reset before and after the scope.
- Observability listeners are process-wide. Each scenario should own its collectors and filter the
  signals it expects instead of retaining a collector across parallel scenarios.
- Container and emulator tests should avoid fixed host ports and globally reused resource names.
  A shared container is safe only when each scenario has an isolated logical resource and cleanup
  cannot affect another scenario.
- Files created by tests need unique transient paths. Transfer ownership to the scenario when the
  file should be removed during cleanup.

## Run snapshots across target frameworks

Verified snapshots are the reviewed contract shared by every supported framework. A mismatch uses
a target-framework-qualified received name, such as `.received.net8.0.json` or
`.received.net10.0.json`. One target removes only its own received file after a match, so concurrent
framework runs do not erase each other's failure evidence.

Snapshot writes that target the same verified path are coordinated inside one process, but two
different tests still must not own different contracts under the same name. Use a unique logical
name or variant for every parameter case. See
[snapshot files, names, and parallel runs](../guides/snapshots/files-and-naming.md).

## Diagnose concurrency failures

Intermittent failures commonly indicate a missing ownership boundary. Check for:

- A second scope waiting because the first scope or scenario result was not disposed.
- Rules, recorded calls, or telemetry left in a singleton that is not a scenario resource.
- Two factory instances recreating or deleting the same database.
- Fixed ports or broker and storage names shared across target-framework processes.
- Multiple parameter cases resolving to the same snapshot path.
- Cleanup from one test disposing an object still used by another.

First reproduce with the same target-framework and runner parallelism used in CI. Temporarily
serializing tests can confirm a collision, but the lasting fix is distinct ownership or a deliberate
shared-resource fixture with one lifecycle.

For contributor commands and canonical-example execution, see
[building and releasing](../contributing/building-and-releasing.md) and
[documentation examples](../contributing/documentation-examples.md).
