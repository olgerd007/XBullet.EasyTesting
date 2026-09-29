# Documentation coverage baseline

This inventory is the Milestone 0 baseline for documentation work. It maps the current public
capabilities to existing source material and records the most important documentation gaps. Update
it whenever a public feature is added, removed, or substantially changed.

## Status key

- **Detailed:** the current documentation explains setup, behavior, variants, and safety.
- **Partial:** documentation exists, but important behavior, examples, or diagnostics are missing.
- **Minimal:** only installation and a short example or summary exist.
- **Missing:** there is no focused user documentation.

These labels describe user documentation, not implementation quality or test coverage.

## Ownership

Ownership is role-based until maintainers assign individuals:

- The documentation maintainer owns the root README, documentation home, getting-started pages,
  shared concepts, terminology, navigation, and validation tooling.
- The maintainer of the package named in a coverage row owns its package README, detailed guides,
  public XML comments, limitations, and canonical examples.
- Core and extension package maintainers jointly own cross-package guides; the maintainer of the
  entry-point package is the primary reviewer.
- The release maintainer owns versioned publication, migration-note checks, and release links.

The source-material and target columns below complete the initial assignment for each planned area.

## Package baseline

| Package | Major capabilities | Current documentation | Canonical source material | Primary gap | Target |
| --- | --- | --- | --- | --- | --- |
| `XBullet.EasyTesting` | Authenticated hosts, composable hosts, users and identities, schemes, scenarios, resources, response assertions | Detailed: [getting started](../getting-started/first-controller-test.md), [core hub](../guides/authentication-and-scenarios.md), [concepts](../concepts/test-hosts.md), and focused task guides | `DocumentationExamples`, `AuthenticationScenarioTests`, `ComposableHostTests`, `EndToEndAuthenticationTests`, `TestScenarioScopeTests` | Maintain canonical examples as APIs evolve | M3 |
| `XBullet.EasyTesting.EntityFrameworkCore` | EF-backed factories, Startup hosts, in-memory provider, scoped database actions, database lifecycle, SQLite cleanup | Detailed: [EF Core guide](../guides/entity-framework-core.md) and [package README](../../src/XBullet.EasyTesting.EntityFrameworkCore/README.md) | `DatabaseControllerTests`, `InMemoryDatabaseTests`, `OrderRouteTests` | Add provider-specific recipes when new modules are introduced | M3 |
| `XBullet.EasyTesting.Http` | Request matching, response building and sequences, faults, delays, cancellation, bounded exchange and body capture, verification | Detailed: [outbound HTTP guide](../guides/outbound-http.md) and [package README](../../src/XBullet.EasyTesting.Http/README.md) | `StubHttpMessageHandlerTests`, `ExternalApiControllerTests` | Maintain matcher, capture-limit, fault, and redaction examples as APIs evolve | M4 |
| `XBullet.EasyTesting.Messaging` | Transport-neutral recording, transport names, typed payload and header assertions | Detailed: [messaging guide](../guides/messaging.md) and [package README](../../src/XBullet.EasyTesting.Messaging/README.md) | `RecordedMessageBusTests`, `PublishingControllerTests` | Add broker-specific adapter recipes as supported transports evolve | M4 |
| `XBullet.EasyTesting.Observability` | Log, activity, event, and metric collection; assertions; deterministic time | Detailed: [observability guide](../guides/observability.md) and [package README](../../src/XBullet.EasyTesting.Observability/README.md) | `ObservabilityTests` | Maintain per-signal, cleanup, and diagnostic examples as APIs evolve | M4 |
| `XBullet.EasyTesting.Azure` | Azure responses, paging, test credentials, dependency injection, pipeline transport and request verification | Detailed: [Azure SDK guide](../guides/azure-sdk.md) and [package README](../../src/XBullet.EasyTesting.Azure/README.md) | `AzureTestingTests` | Add service-specific recipes when new SDK integrations are introduced | M4 |
| `XBullet.EasyTesting.Testcontainers` | Built-in service modules, arbitrary modules, scenario resources, runtime verification | Detailed: [container infrastructure guide](../guides/testcontainers.md) and [package README](../../src/XBullet.EasyTesting.Testcontainers/README.md) | `TestcontainerTests`, `ContainerModuleSmokeTests` | Maintain service images, readiness, cleanup, and runtime commands as modules evolve | M5 |
| `XBullet.EasyTesting.Aspire` | Distributed application startup, resource discovery, readiness, endpoints, logs and diagnostics | Detailed: [Aspire guide](../guides/aspire.md) and [package README](../../src/XBullet.EasyTesting.Aspire/README.md) | `AspireTests` | Maintain resource APIs, timeout behavior, and diagnostics as Aspire evolves | M5 |
| `XBullet.EasyTesting.AzureFunctions` | Test host, contexts, middleware, bindings, retry state, invocation results, trigger builders, Durable activity dispatch | Detailed: [host guide](../guides/azure-functions.md), [trigger recipes](../guides/azure-functions-triggers.md), [Durable boundary](../guides/azure-functions-durable.md), and [package README](../../src/XBullet.EasyTesting.AzureFunctions/README.md) | `AzureFunctionScopeTests`, `FunctionTriggerTests`, `DurableFunctionTests` | Maintain trigger metadata and explicitly supported Durable surface as worker APIs evolve | M5 |
| `XBullet.EasyTesting.Snapshots.Core` | JSON, text, HTTP responses and exchanges, transformations, defaults, diagnostics, acceptance and maintenance | Detailed: [snapshot workflow](../guides/snapshots.md) and [package README](../../src/XBullet.EasyTesting.Snapshots/README.md) | `SnapshotAssertTests`, `BuiltInSnapshotTests`, `CrudControllerSnapshotTests` | Maintain task guides and canonical transformation and maintenance examples | M6 |
| `XBullet.EasyTesting.Snapshots.Http` | Snapshots for stub requests and complete outbound exchanges, request/response options | Detailed: [snapshot recipes](../guides/snapshots/recipes.md#outbound-http-stub-requests-and-exchanges) and [package README](../../src/XBullet.EasyTesting.Snapshots.Http/README.md) | `SnapshotAssertTests`, `ExternalApiControllerTests` | Maintain formats, security defaults, and interoperation examples | M6 |
| `XBullet.EasyTesting.Snapshots` | Compatibility facade forwarding snapshot core and HTTP APIs | Detailed: [migration guide](../guides/snapshots/migration.md) and [package README](../../src/XBullet.EasyTesting.Snapshots.Compatibility/README.md) | `SnapshotAssertTests` type-forward baseline | Preserve compatibility guarantees and version-alignment guidance | M6 |
| `XBullet.EasyTesting.Verify.Xunit` | Verify.Xunit v3 controller-response snapshot adapter | Detailed: [Verify guide](../guides/verify-xunit.md), [engine comparison](../guides/snapshots/choose-engine.md), and [package README](../../src/XBullet.EasyTesting.Verify.Xunit/README.md) | `ControllerSnapshotTests` | Maintain recorder behavior and Verify-version compatibility | M6 |

Test names in this table refer to files under `tests/`. Canonical documentation examples use named
source regions synchronized into Markdown by `eng/sync-documentation-snippets.ps1`.

## Cross-cutting concept coverage

| Concept | Current source | Status | Needed canonical page | Target |
| --- | --- | --- | --- | --- |
| Choose packages | [Documentation home](../index.md#choose-packages) | Detailed | Maintain package boundaries and combinations | M1 |
| First authenticated controller test | [Executable getting-started tutorial](../getting-started/first-controller-test.md) | Detailed | Maintain and extend from canonical source regions | M2 |
| Host selection | [Test-host concept](../concepts/test-hosts.md) | Detailed | Maintain selection and lifecycle guidance | M3 |
| Scenario lifecycle and isolation | [Scenario concept](../concepts/scenarios-and-isolation.md) | Detailed | Maintain concurrency and diagnostics guidance | M3 |
| Authentication models | [Authentication concept](../concepts/authentication.md) | Detailed | Maintain simulated and end-to-end recipes | M3 |
| Resources, ownership, and cleanup | [Resource concept](../concepts/resources-and-cleanup.md) | Detailed | Maintain lifecycle and ownership rules | M3 |
| Core failure diagnostics | Scenario, response-assertion, resource, and EF guides | Detailed | Extend package-specific diagnostics in later milestones | M3-M6 |
| Security and redaction | [Sensitive-data safety](../concepts/security-and-sensitive-data.md) with package-specific guidance | Detailed | Maintain the shared threat model and package links as captured surfaces evolve | M4-M6 |
| Multi-target and parallel behavior | [Multi-target and parallel execution](../concepts/multi-target-and-parallel-execution.md) with scenario and snapshot guidance | Detailed | Maintain ownership boundaries as supported frameworks and resources evolve | M3-M6 |
| Built-in versus Verify snapshots | [Snapshot engine comparison](../guides/snapshots/choose-engine.md) | Detailed | Maintain decision criteria and workflow ownership guidance | M6 |
| API reference | [Generated package and namespace reference](../api/index.md) | Detailed | Keep strict Docfx generation and package entry points aligned with public APIs | M7 |
| Documentation quality automation | [Maintenance policy](documentation-maintenance.md) | Detailed | Keep CI gates, review dates, and release checks aligned with repository workflows | M8 |

## Duplication and consistency findings

Milestone 1 removed duplication from the root entry point and replaced generic repository links.
The remaining guide/package overlap is tracked for the feature-focused milestones. NuGet READMEs
must retain enough standalone content to be useful on package pages.

| Area | Finding | Canonical resolution | Status |
| --- | --- | --- | --- |
| Authentication | Basic, multi-scheme, hybrid, and end-to-end flows previously made the root README difficult to navigate | Keep one short root example, use concept and recipe pages for modes, retain only the package minimum in the NuGet README | Resolved in M3 |
| EF Core | Provider choice and database lifecycle previously appeared in both the extracted guide and package README | Make the detailed EF guide canonical; package README summarizes provider choice and links to it | Resolved in M3 |
| HTTP and messaging | Examples needed focused recipes beyond their package-level minimums | Keep the application-boundary page as a hub and use the HTTP and messaging guides as canonical recipes | Resolved in M4 |
| Azure Functions | Package and detailed guides overlapped while trigger behavior primarily lived in tests | Keep the package minimum in the README; use the host, trigger, and Durable guides for canonical behavior | Resolved in M5 |
| Snapshots | The extracted guide and core package README contained substantial overlapping workflows | Keep the package page concise and split detailed work into getting-started, naming, recipes, stabilization, defaults, maintenance, migration, and engine-selection pages | Resolved in M6 |
| Verify.Xunit | The extracted guide and package page repeated some setup and recorder behavior | Keep package setup in the package README and comparison/workflow detail in snapshot guides | Resolved in M6 |
| Package naming | The source directory is `XBullet.EasyTesting.Snapshots`, while the lightweight package is `XBullet.EasyTesting.Snapshots.Core` and a separate project provides the compatibility facade | Always use exact package IDs in prose and explain the split on one migration page | Resolved in M6 |
| Generic repository links | Package READMEs previously linked to the repository root for "complete examples" | Link directly to focused guides and canonical executable examples | Resolved in M1 |

## Limitations register

The following constraints are already established by documentation or tests and must remain visible
on the relevant canonical pages. Validate this register against implementation whenever behavior
changes.

| Area | Limitation or boundary | Required documentation location |
| --- | --- | --- |
| Simulated authentication | Test authentication is installed only in the test host and does not prove that production token or API-key validation works | Authentication concept and host guide |
| Credential injection | Real API-key helpers transport a value; the application's handler remains responsible for parsing and validation | End-to-end authentication guide |
| EF Core in-memory | The provider does not enforce relational constraints or support transactions | EF provider-selection guide and package README |
| Database lifecycle | The default per-scenario lifecycle is `EnsureDeleted` followed by `EnsureCreated`; applications using migrations or shared databases must override it | EF lifecycle guide |
| Testcontainers | Tests require a Docker-compatible runtime; the Service Bus emulator requires explicit license acceptance | Testcontainers prerequisites and Service Bus guide |
| Optional real-service tests | Real container tests are intentionally separate from the ordinary unit-test run | Testcontainers CI guide |
| Durable Functions | Only `CallActivityAsync` is emulated; timers, external events, sub-orchestrators, and replay state are unsupported | Durable Functions guide and package README |
| Snapshot package boundary | Snapshot core has no dependency on outbound HTTP stubs; stub request/exchange adapters require `XBullet.EasyTesting.Snapshots.Http` | Snapshot package-selection and migration guides |
| Snapshot updates | `all` update mode can replace verified files and must not be enabled in a normal CI verification run; CI writes require explicit authorization | Snapshot acceptance and CI guide |
| Sensitive data | Authentication headers, cookies, API keys, tokens, and sensitive query values must be excluded or redacted before recording or snapshotting | HTTP, observability, Azure, and snapshot safety sections |
| Verify recorder | Controller snapshots describe a real `TestServer` call; they do not replace transport with an outbound HTTP stub | Verify guide |
| Multi-target snapshots | Target-framework-specific received names prevent concurrent target runs from overwriting failure output | Snapshot locations and parallelism guide |

## Feature documentation definition of done

Use the [feature-guide template](../feature-guide-template.md) and the review checklist in the
[style guide](../documentation-style-guide.md#review-checklist). At a minimum, a coverage row moves
to **Detailed** only when it has:

- A discoverable, focused guide.
- A verified minimal example and a realistic example.
- Documented defaults, lifecycle, diagnostics, and limitations.
- Links to related concepts and public API reference.
- Passing automated documentation checks once those checks are introduced.
