# Changelog

Notable changes to XBullet.EasyTesting are documented in this file.

The project follows Semantic Versioning. Package versions are produced from GitHub Release tags.

## [1.0.7] - 2026-09-19

### Added

- Explicit .NET 9 package targeting and CI coverage alongside .NET 8 and .NET 10.
- Opt-in preservation of an application's default authentication scheme, named simulated schemes,
  ASP.NET Core Identity user seeding and principal linkage, and transport of real Identity bearer
  tokens without JWT-specific test configuration.
- Early minimal-host settings for connection strings and other values consumed during top-level
  startup.
- EF Core context-factory replacement, custom scenario database lifecycle hooks, and fluent custom
  recreation callbacks.
- SQLite pool clearing, transient file-lock retries, and terminal cleanup diagnostics.
- Guidance for hybrid simulated-identity and real-handler testing in one test project.

## [1.0.6] - 2026-09-18

### Added

- Configurable snapshot placement with `BesideSourceFile()` and a context-aware directory resolver.
- Plain-text snapshot assertions with `.txt` files and the existing update, diff, catalog, and
  maintenance workflow.
- Runtime-qualified received files for safe parallel multi-targeted test runs, plus deterministic
  filename shortening and hashed parameter variants.
- Default redaction of sensitive query parameters, outbound-request header redaction, and safe
  merging of duplicate response and content headers.
- Snapshot test coverage hardened across public HTTP assertions, structured diagnostics, redaction,
  location, acceptance, and maintenance paths, with enforced CI coverage thresholds.

## [1.0.5] - 2026-09-18

### Added

- Structured raw JSON and HTTP-content snapshot assertions backed by `System.Text.Json`, producing
  `.received.json` and `.verified.json` files.
- Lossless JSON body capture for controller responses and recorded HTTP requests, including large
  and high-precision number tokens and strict structured JSON media-type recognition.
- Atomic, parallel-safe snapshot writes; portable collision-resistant filenames; and named
  snapshot variants for parameterized tests and multiple assertions in one test method.
- Extended JSON Pointer transformations for targeted scrubbing, ignoring, replacement, hashing,
  array sorting, and opt-in canonical object ordering, with strict date handling and scrubber
  output validation.
- Safer HTTP snapshots with header omission and redaction, expanded sensitive-header defaults,
  response-body JSON assertions, and repeatable verification after seekable content is consumed.
- JSONPath mismatch diagnostics with expected and actual values; instance-scoped obsolete snapshot
  tracking and reusable defaults; guarded bulk maintenance; and explicit CI update authorization.
- Split snapshots into lightweight `XBullet.EasyTesting.Snapshots.Core` and outbound-request
  `XBullet.EasyTesting.Snapshots.Http` packages, with the original package retained as a
  type-forwarding compatibility facade.
- A bulk external-catalog synchronization workflow in the sample test API that upserts products
  and persists success or failure audit records, with integration coverage for outbound HTTP,
  database state, validation, and upstream failures.
- A Kafka-triggered order-pricing function sample that calls an external HTTP API, records the
  enriched total, and preserves upstream exceptions for broker retry behavior.

## [1.0.4] - 2026-09-16

### Added

- Startup-based authenticated and EF Core test hosts that run `IntegrationTestStartup`-style
  pipelines on `TestServer` without invoking `Program.Main`, while preserving injected
  configuration and per-scenario database support.
- A Federation authentication profile with an additional API-user identity and replaceable claims
  principal materialization for applications that require concrete identity subclasses.
- A pre-host asynchronous scenario-environment lifecycle for external dependencies, including
  dynamic configuration and service registration, typed resource lookup, failure diagnostics,
  reverse-order cleanup, and fluent or factory-derived registration.
- An initial `XBullet.EasyTesting.Observability` package with bounded structured `ILogger`,
  `Activity`, and metric capture; fluent assertions; scenario failure diagnostics; source and meter
  filtering; and optional deterministic `FakeTimeProvider` registration.
- An initial `XBullet.EasyTesting.Testcontainers` package with generic scenario-owned containers,
  native readiness checks, dynamic endpoint injection, bounded failure diagnostics, and convenient
  PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, and Service Bus emulator registrations,
  backed by explicit real-service smoke tests that can be run on demand.
- An initial `XBullet.EasyTesting.Aspire` package for closed-box distributed tests with AppHost
  customization, resource health waits, endpoint and connection-string access, bounded resource
  logs, state-aware failure diagnostics, and deterministic asynchronous cleanup.
- An initial `XBullet.EasyTesting.Azure` package with concrete Azure SDK responses, response and
  pageable factories, a deterministic recording token credential, direct client replacement, and a
  scripted HTTP pipeline transport with request verification and redacted scenario diagnostics.

### Security

- Enforced NuGet auditing for moderate, high, and critical vulnerabilities.
- Enabled all built-in .NET security analyzers and added CodeQL and dependency-review workflows.
- Pinned every GitHub Action to an immutable commit SHA.

## [1.0.3] - 2026-09-15

### Added

- A first-class EF Core in-memory test factory with automatic per-scenario database isolation.
- Multi-targeted packages and test coverage for .NET 8 and .NET 10.
- Package validation against the latest stable release to detect binary compatibility breaks.
- Public API approval baselines for every package.
- Windows and Ubuntu CI test coverage with Cobertura coverage artifacts.
- Focused NuGet README content for each package.
- Test-framework-agnostic fluent assertions for scenario responses and recorded messages.

## [0.2.0] - 2026-09-15

### Added

- Authenticated ASP.NET Core controller test hosting with fluent user profiles.
- Optional end-to-end JWT, API-key, and client-certificate authentication using locally signed tokens, OIDC backchannel validation, JWKS rotation, named authorities, negative token scenarios, redacted event assertions and diagnostics, saved access tokens, real credential injection, composite identities, and authentication properties.
- Entity Framework Core database scenarios.
- A composable `EasyTestHost` builder and arrange/client/request scenario API.
- Per-test `TestScenarioScope` isolation for databases, mutable test resources, service/configuration overrides, and pre-cleanup failure diagnostics.
- Outbound HTTP stubs with query, header, structural JSON body/property/path, and custom request predicates, response sequences, delays, timeouts, cancellation and malformed-response faults, per-rule mismatch diagnostics, dynamic responses, call verification, and request snapshots.
- Transport-neutral message recording.
- Built-in snapshots and an optional Verify.Xunit adapter.
- Isolated Azure Functions test host with complete function contexts, captured input/output bindings, retry construction, middleware execution, multiple-output assertions, and fluent HTTP, timer, Kafka, Service Bus, Queue Storage, Blob, Event Grid, and Event Hubs trigger data.
- CI preview packages and guarded stable/pre-release NuGet publishing flows.
