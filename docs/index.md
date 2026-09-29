# XBullet.EasyTesting documentation

XBullet.EasyTesting provides composable infrastructure for integration testing ASP.NET Core
applications, distributed applications, Azure Functions, external dependencies, and observable
side effects. All packages target .NET 8, .NET 9, and .NET 10.

Use this page to choose the smallest set of packages for a testing goal. Documentation completeness
and maintenance ownership are tracked in the
[coverage matrix](contributing/documentation-coverage.md).

Use the [searchable API reference](api/index.md) when you know the package, namespace, type, or
member you need.

## Start here

If you are testing an ASP.NET Core endpoint for the first time:

1. Install [`XBullet.EasyTesting`](../src/XBullet.EasyTesting/README.md#install).
2. Follow [the first controller test](getting-started/first-controller-test.md) to create an
   authenticated factory, scenario scope, client, request, and assertion.
3. Give each test an isolated scenario when it changes application state; see
   [scenarios and isolation](concepts/scenarios-and-isolation.md).
4. Add extension packages only for the external boundaries that the test needs.

The core package supports both `Program`-based and `Startup`-based applications. It can simulate
identities inside `TestServer` or send credentials through the application's real authentication
handlers. These are different kinds of tests: simulated authentication is fast and focused, while
end-to-end authentication validates token, certificate, or API-key handling.

## Find documentation by goal

| I want to... | Start with | Package |
| --- | --- | --- |
| Test an authenticated controller | [Run your first controller test](getting-started/first-controller-test.md) | `XBullet.EasyTesting` |
| Isolate setup and cleanup for every test | [Scenarios and isolation](concepts/scenarios-and-isolation.md) | `XBullet.EasyTesting` |
| Test a `Startup`-based application without executing `Program.Main` | [Choose a test host](concepts/test-hosts.md#host-a-startup-application) | `XBullet.EasyTesting` |
| Test multiple authentication schemes | [Authentication models](concepts/authentication.md#map-named-schemes) | `XBullet.EasyTesting` |
| Exercise production authentication handlers | [Configure end-to-end handlers](concepts/authentication.md#configure-end-to-end-handlers) | `XBullet.EasyTesting` |
| Replace services or configuration | [Override services and configuration](guides/service-and-configuration-overrides.md) | `XBullet.EasyTesting` |
| Assert status, headers, or JSON | [Assert controller responses](guides/response-assertions.md) | `XBullet.EasyTesting` |
| Seed or inspect an EF Core database | [Test with Entity Framework Core](guides/entity-framework-core.md) | `XBullet.EasyTesting.EntityFrameworkCore` |
| Stub an outbound HTTP API | [Test outbound HTTP dependencies](guides/outbound-http.md) | `XBullet.EasyTesting.Http` |
| Record messages published by the application | [Test published messages](guides/messaging.md) | `XBullet.EasyTesting.Messaging` |
| Assert logs, traces, or metrics | [Test logs, traces, metrics, and time](guides/observability.md) | `XBullet.EasyTesting.Observability` |
| Test Azure SDK clients without a live service | [Test Azure SDK clients](guides/azure-sdk.md) | `XBullet.EasyTesting.Azure` |
| Run real infrastructure for a scenario | [Test with containerized infrastructure](guides/testcontainers.md) | `XBullet.EasyTesting.Testcontainers` |
| Test an Aspire distributed application | [Test an Aspire distributed application](guides/aspire.md) | `XBullet.EasyTesting.Aspire` |
| Test .NET isolated Azure Functions | [Test .NET isolated Azure Functions](guides/azure-functions.md) | `XBullet.EasyTesting.AzureFunctions` |
| Assert JSON, text, controller responses, or exchanges | [Create and review a snapshot](guides/snapshots/getting-started.md) | `XBullet.EasyTesting.Snapshots.Core` |
| Snapshot outbound requests captured by HTTP stubs | [Outbound HTTP snapshot recipe](guides/snapshots/recipes.md#outbound-http-stub-requests-and-exchanges) | `XBullet.EasyTesting.Snapshots.Http` |
| Choose built-in snapshots or Verify.Xunit | [Compare snapshot engines](guides/snapshots/choose-engine.md) | Core or `XBullet.EasyTesting.Verify.Xunit` |
| Keep credentials and personal data out of artifacts | [Protect sensitive test data](concepts/security-and-sensitive-data.md) | All packages |
| Run tests safely across frameworks and in parallel | [Design multi-target and parallel tests](concepts/multi-target-and-parallel-execution.md) | All packages |

## Choose packages

Install only the packages required by the test project.

For core behavior, see [test-host selection](concepts/test-hosts.md),
[authentication models](concepts/authentication.md), [scenarios and isolation](concepts/scenarios-and-isolation.md),
[resources and cleanup](concepts/resources-and-cleanup.md),
[sensitive-data safety](concepts/security-and-sensitive-data.md), and
[multi-target and parallel execution](concepts/multi-target-and-parallel-execution.md).

### Application host and persistence

| Package | Use it when... | Usually combined with |
| --- | --- | --- |
| [`XBullet.EasyTesting`](../src/XBullet.EasyTesting/README.md) | You need an in-memory ASP.NET Core host, test identities, scenario isolation, or response assertions | Any extension package |
| [`XBullet.EasyTesting.EntityFrameworkCore`](../src/XBullet.EasyTesting.EntityFrameworkCore/README.md) | A scenario must seed, query, recreate, or clean up an EF Core database | Core package and optionally Testcontainers |

### Application boundaries and diagnostics

| Package | Use it when... | Usually combined with |
| --- | --- | --- |
| [`XBullet.EasyTesting.Http`](../src/XBullet.EasyTesting.Http/README.md) | The application calls an external HTTP service that the test should stub and inspect | Core package; optionally snapshot HTTP adapters |
| [`XBullet.EasyTesting.Messaging`](../src/XBullet.EasyTesting.Messaging/README.md) | The application publishes messages whose transport, destination, headers, or payload must be asserted | Core package |
| [`XBullet.EasyTesting.Observability`](../src/XBullet.EasyTesting.Observability/README.md) | A test must assert structured logs, distributed traces, metrics, or deterministic time | Core package |
| [`XBullet.EasyTesting.Azure`](../src/XBullet.EasyTesting.Azure/README.md) | Code uses Azure SDK clients and needs deterministic responses, paging, credentials, or pipeline transport | Core package when used in hosted scenarios |

### Runtime environments

| Package | Use it when... | Runtime prerequisite |
| --- | --- | --- |
| [`XBullet.EasyTesting.Testcontainers`](../src/XBullet.EasyTesting.Testcontainers/README.md) | A test needs a real PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, Service Bus emulator, or custom container; see the [infrastructure guide](guides/testcontainers.md) | Docker-compatible container runtime |
| [`XBullet.EasyTesting.Aspire`](../src/XBullet.EasyTesting.Aspire/README.md) | A closed-box test must start an Aspire application, discover resources, wait for readiness, and collect diagnostics; see the [Aspire guide](guides/aspire.md) | An Aspire app host |
| [`XBullet.EasyTesting.AzureFunctions`](../src/XBullet.EasyTesting.AzureFunctions/README.md) | A test directly invokes .NET isolated functions or constructs trigger, binding, middleware, retry, or Durable activity state; see the [Functions guide](guides/azure-functions.md) | .NET isolated worker application |

### Snapshot assertions

| Package | Use it when... | Important boundary |
| --- | --- | --- |
| [`XBullet.EasyTesting.Snapshots.Core`](../src/XBullet.EasyTesting.Snapshots/README.md) | You want framework-independent JSON, text, controller-response, or complete HTTP-exchange snapshots; see the [workflow hub](guides/snapshots.md) | Does not contain adapters for `XBullet.EasyTesting.Http` stubs |
| [`XBullet.EasyTesting.Snapshots.Http`](../src/XBullet.EasyTesting.Snapshots.Http/README.md) | You want snapshots of requests and exchanges recorded by `StubHttpMessageHandler`; see the [recipe](guides/snapshots/recipes.md#outbound-http-stub-requests-and-exchanges) | Requires the HTTP package and snapshot core |
| [`XBullet.EasyTesting.Snapshots`](../src/XBullet.EasyTesting.Snapshots.Compatibility/README.md) | Existing code needs the compatibility facade over both snapshot packages; see the [migration guide](guides/snapshots/migration.md) | Prefer the specific core and HTTP packages for new projects |
| [`XBullet.EasyTesting.Verify.Xunit`](../src/XBullet.EasyTesting.Verify.Xunit/README.md) | A test suite uses Verify.Xunit v3 and needs controller-response integration; see the [engine comparison](guides/snapshots/choose-engine.md) | Uses Verify's approval workflow rather than the built-in snapshot engine |

The built-in and Verify snapshot integrations can coexist. When a test suite uses both, document
which workflow owns each snapshot so file naming, review, and acceptance remain predictable.

## Important testing boundaries

- Simulated test users validate authorization behavior, not production credential validation. Use
  the end-to-end authentication path when token, certificate, or API-key parsing is part of the
  behavior under test.
- The EF Core in-memory provider does not reproduce relational constraints or transactions. Use
  SQLite or the production provider when those behaviors matter.
- Testcontainers requires a Docker-compatible runtime. Service Bus emulator scenarios also require
  explicit license acceptance.
- Durable Functions support is intentionally focused on `CallActivityAsync`; timers, external
  events, sub-orchestrators, and replay behavior require another testing layer.
- Snapshot and diagnostic output can contain sensitive application data. Configure exclusions or
  redaction before committing files or publishing logs; follow the
  [sensitive-data safety model](concepts/security-and-sensitive-data.md).
- Multi-target and parallel test runs need explicit ownership for databases, recorders, collectors,
  files, ports, and infrastructure. See
  [multi-target and parallel execution](concepts/multi-target-and-parallel-execution.md).

See the maintained
[limitations register](contributing/documentation-coverage.md#limitations-register) for the full
baseline and the package guides for feature-specific constraints.

## Examples and source

The repository currently uses integration tests as the primary executable examples:

- [`TestApi.IntegrationTests`](../tests/TestApi.IntegrationTests/DocumentationExamples.cs) covers ASP.NET Core hosts,
  authentication, EF Core, outbound HTTP, messaging, observability, Testcontainers, and snapshots.
- [`TestStartupApi.IntegrationTests`](../tests/TestStartupApi.IntegrationTests/OrderRouteTests.cs) covers
  `Startup`-based hosts and larger composed scenarios.
- [`TestFunctions.IntegrationTests`](../tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs) covers Azure
  Functions hosts, triggers, middleware, bindings, and Durable activity dispatch.
- [`XBullet.EasyTesting.Tests`](../tests/XBullet.EasyTesting.Tests/SnapshotAssertTests.cs) covers
  lower-level HTTP, Azure,
  messaging, and snapshot behavior.

Canonical documentation examples use named source regions and are synchronized into Markdown by
`eng/sync-documentation-snippets.ps1`. See
[how to add executable documentation examples](contributing/documentation-examples.md).

## Contribute to the documentation

Follow the [documentation style guide](documentation-style-guide.md) and start a new task-oriented
page from the [feature-guide template](feature-guide-template.md). The
[coverage baseline](contributing/documentation-coverage.md) records current gaps and maintenance
ownership. See the
[maintenance policy](contributing/documentation-maintenance.md) for automated checks and review
cadence, and use the [release checklist](contributing/release-checklist.md) before tagging packages.
