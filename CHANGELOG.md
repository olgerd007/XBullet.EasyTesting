# Changelog

Notable changes to XBullet.EasyTesting are documented in this file.

The project follows Semantic Versioning. Package versions are produced from GitHub Release tags.

## [Unreleased]

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
