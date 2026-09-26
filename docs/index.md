# XBullet.EasyTesting documentation

XBullet.EasyTesting provides composable infrastructure for integration testing ASP.NET Core
applications, distributed applications, Azure Functions, external dependencies, and observable
side effects. All packages target .NET 8, .NET 9, and .NET 10.

Use this page to choose the smallest set of packages for a testing goal. The detailed documentation
is being expanded according to the [documentation roadmap](documentation-roadmap.md); links below
lead to the best current guide or package README.

## Start here

If you are testing an ASP.NET Core endpoint for the first time:

1. Install [`XBullet.EasyTesting`](../src/XBullet.EasyTesting/README.md#install).
2. Create an authenticated test factory and client by following the
   [controller authentication example](guides/authentication-and-scenarios.md#controller-authentication).
3. Give each test an isolated scenario when it changes application state; see
   [per-test isolation](guides/authentication-and-scenarios.md#per-test-isolation).
4. Add extension packages only for the external boundaries that the test needs.

The core package supports both `Program`-based and `Startup`-based applications. It can simulate
identities inside `TestServer` or send credentials through the application's real authentication
handlers. These are different kinds of tests: simulated authentication is fast and focused, while
end-to-end authentication validates token, certificate, or API-key handling.

## Find documentation by goal

| I want to... | Start with | Package |
| --- | --- | --- |
| Test an authenticated controller | [Controller authentication](guides/authentication-and-scenarios.md#controller-authentication) | `XBullet.EasyTesting` |
| Isolate setup and cleanup for every test | [Per-test isolation](guides/authentication-and-scenarios.md#per-test-isolation) | `XBullet.EasyTesting` |
| Test a `Startup`-based application without executing `Program.Main` | [Startup-based hosts](guides/authentication-and-scenarios.md#startup-based-hosts) | `XBullet.EasyTesting` |
| Test multiple authentication schemes | [Multiple authentication schemes](guides/authentication-and-scenarios.md#multiple-authentication-schemes) | `XBullet.EasyTesting` |
| Exercise production authentication handlers | [End-to-end authentication](guides/authentication-and-scenarios.md#end-to-end-authentication) | `XBullet.EasyTesting` |
| Seed or inspect an EF Core database | [Entity Framework Core package guide](../src/XBullet.EasyTesting.EntityFrameworkCore/README.md) | `XBullet.EasyTesting.EntityFrameworkCore` |
| Stub an outbound HTTP API | [HTTP package guide](../src/XBullet.EasyTesting.Http/README.md) | `XBullet.EasyTesting.Http` |
| Record messages published by the application | [Messaging package guide](../src/XBullet.EasyTesting.Messaging/README.md) | `XBullet.EasyTesting.Messaging` |
| Assert logs, traces, or metrics | [Observability package guide](../src/XBullet.EasyTesting.Observability/README.md) | `XBullet.EasyTesting.Observability` |
| Test Azure SDK clients without a live service | [Azure package guide](../src/XBullet.EasyTesting.Azure/README.md) | `XBullet.EasyTesting.Azure` |
| Run real infrastructure for a scenario | [Testcontainers package guide](../src/XBullet.EasyTesting.Testcontainers/README.md) | `XBullet.EasyTesting.Testcontainers` |
| Test an Aspire distributed application | [Aspire package guide](../src/XBullet.EasyTesting.Aspire/README.md) | `XBullet.EasyTesting.Aspire` |
| Test .NET isolated Azure Functions | [Azure Functions package guide](../src/XBullet.EasyTesting.AzureFunctions/README.md) | `XBullet.EasyTesting.AzureFunctions` |
| Assert JSON, HTTP responses, or exchanges with built-in snapshots | [Snapshot core package guide](../src/XBullet.EasyTesting.Snapshots/README.md) | `XBullet.EasyTesting.Snapshots.Core` |
| Snapshot outbound requests captured by HTTP stubs | [Snapshot HTTP package guide](../src/XBullet.EasyTesting.Snapshots.Http/README.md) | `XBullet.EasyTesting.Snapshots.Http` |
| Use Verify.Xunit for controller snapshots | [Verify.Xunit package guide](../src/XBullet.EasyTesting.Verify.Xunit/README.md) | `XBullet.EasyTesting.Verify.Xunit` |

## Choose packages

Install only the packages required by the test project.

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
| [`XBullet.EasyTesting.Testcontainers`](../src/XBullet.EasyTesting.Testcontainers/README.md) | A test needs a real PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, Service Bus emulator, or custom container | Docker-compatible container runtime |
| [`XBullet.EasyTesting.Aspire`](../src/XBullet.EasyTesting.Aspire/README.md) | A closed-box test must start an Aspire application, discover resources, wait for readiness, and collect diagnostics | An Aspire app host |
| [`XBullet.EasyTesting.AzureFunctions`](../src/XBullet.EasyTesting.AzureFunctions/README.md) | A test directly invokes .NET isolated functions or constructs trigger, binding, middleware, retry, or Durable activity state | .NET isolated worker application |

### Snapshot assertions

| Package | Use it when... | Important boundary |
| --- | --- | --- |
| [`XBullet.EasyTesting.Snapshots.Core`](../src/XBullet.EasyTesting.Snapshots/README.md) | You want framework-independent JSON, text, controller-response, or complete HTTP-exchange snapshots | Does not contain adapters for `XBullet.EasyTesting.Http` stubs |
| [`XBullet.EasyTesting.Snapshots.Http`](../src/XBullet.EasyTesting.Snapshots.Http/README.md) | You want snapshots of requests and exchanges recorded by `StubHttpMessageHandler` | Requires the HTTP package and snapshot core |
| [`XBullet.EasyTesting.Snapshots`](../src/XBullet.EasyTesting.Snapshots.Compatibility/README.md) | Existing code needs the compatibility facade over both snapshot packages | Prefer the specific core and HTTP packages for new projects |
| [`XBullet.EasyTesting.Verify.Xunit`](../src/XBullet.EasyTesting.Verify.Xunit/README.md) | A test suite uses Verify.Xunit v3 and needs controller-response integration | Uses Verify's approval workflow rather than the built-in snapshot engine |

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
  redaction before committing files or publishing logs.

See the maintained [limitations register](documentation-coverage.md#limitations-register) for the
full baseline and the package guides for feature-specific constraints.

## Examples and source

The repository currently uses integration tests as the primary executable examples:

- [`TestApi.IntegrationTests`](../tests/TestApi.IntegrationTests) covers ASP.NET Core hosts,
  authentication, EF Core, outbound HTTP, messaging, observability, Testcontainers, and snapshots.
- [`TestStartupApi.IntegrationTests`](../tests/TestStartupApi.IntegrationTests) covers
  `Startup`-based hosts and larger composed scenarios.
- [`TestFunctions.IntegrationTests`](../tests/TestFunctions.IntegrationTests) covers Azure
  Functions hosts, triggers, middleware, bindings, and Durable activity dispatch.
- [`XBullet.EasyTesting.Tests`](../tests/XBullet.EasyTesting.Tests) covers lower-level HTTP, Azure,
  messaging, and snapshot behavior.

Milestone 2 of the roadmap will turn selected tests into stable, reusable documentation snippets.

## Contribute to the documentation

Follow the [documentation style guide](documentation-style-guide.md) and start a new task-oriented
page from the [feature-guide template](feature-guide-template.md). The
[coverage baseline](documentation-coverage.md) records current gaps and target milestones.
