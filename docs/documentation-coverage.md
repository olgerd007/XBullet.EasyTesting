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
| `XBullet.EasyTesting` | Authenticated hosts, composable hosts, users and identities, schemes, scenarios, resources, response assertions | Partial: [root entry point](../README.md), [package README](../src/XBullet.EasyTesting/README.md), [authentication and scenarios guide](guides/authentication-and-scenarios.md) | `AuthenticatedControllerTests`, `AuthenticationScenarioTests`, `ComposableHostTests`, `EndToEndAuthenticationTests`, `TestScenarioScopeTests` | Refine the extracted guide into task-focused pages; explain lifecycle and diagnostic contracts | M3 |
| `XBullet.EasyTesting.EntityFrameworkCore` | EF-backed factories, Startup hosts, in-memory provider, scoped database actions, database lifecycle, SQLite cleanup | Partial: [application-boundary guide](guides/application-boundaries.md), [package README](../src/XBullet.EasyTesting.EntityFrameworkCore/README.md) | `DatabaseControllerTests`, `InMemoryDatabaseTests`, `OrderRouteTests` | Provider-selection guide, migrations, lifecycle overrides, cleanup diagnostics | M3 |
| `XBullet.EasyTesting.Http` | Request matching, response building and sequences, faults, delays, cancellation, exchanges, verification | Partial: [package README](../src/XBullet.EasyTesting.Http/README.md), [application-boundary guide](guides/application-boundaries.md) | `StubHttpMessageHandlerTests`, `ExternalApiControllerTests` | Focused recipes for all matchers and failures; diagnostic examples | M4 |
| `XBullet.EasyTesting.Messaging` | Transport-neutral recording, transport names, typed payload and header assertions | Partial: [package README](../src/XBullet.EasyTesting.Messaging/README.md), [application-boundary guide](guides/application-boundaries.md) | `RecordedMessageBusTests`, `PublishingControllerTests` | Registration/adaptation guide, ordering/count assertions, failure output | M4 |
| `XBullet.EasyTesting.Observability` | Log, activity, event, and metric collection; assertions; deterministic time | Minimal: [package README](../src/XBullet.EasyTesting.Observability/README.md) | `ObservabilityTests` | Per-signal recipes, filters, scopes, disposal, diagnostics, deterministic time | M4 |
| `XBullet.EasyTesting.Azure` | Azure responses, paging, test credentials, dependency injection, pipeline transport and request verification | Partial: [package README](../src/XBullet.EasyTesting.Azure/README.md) | `AzureTestingTests` | Failure recipes, recorded-request assertions, integration with scenario resources | M4 |
| `XBullet.EasyTesting.Testcontainers` | Built-in service modules, arbitrary modules, scenario resources, runtime verification | Partial: [package README](../src/XBullet.EasyTesting.Testcontainers/README.md) | `TestcontainerTests`, `ContainerModuleSmokeTests` | Service-specific setup, readiness, sharing, cleanup, CI and failure diagnostics | M5 |
| `XBullet.EasyTesting.Aspire` | Distributed application startup, resource discovery, readiness, endpoints, logs and diagnostics | Partial: [package README](../src/XBullet.EasyTesting.Aspire/README.md) | `AspireTests` | Resource/endpoint recipes, timeout and shutdown behavior, troubleshooting | M5 |
| `XBullet.EasyTesting.AzureFunctions` | Test host, contexts, middleware, bindings, retry state, invocation results, trigger builders, Durable activity dispatch | Partial: [detailed guide](guides/azure-functions.md), [package README](../src/XBullet.EasyTesting.AzureFunctions/README.md) | `AzureFunctionScopeTests`, `FunctionTriggerTests`, `DurableFunctionTests` | Host lifecycle, each trigger, output assertions, middleware, unsupported Durable behavior | M5 |
| `XBullet.EasyTesting.Snapshots.Core` | JSON, text, HTTP responses and exchanges, transformations, defaults, diagnostics, acceptance and maintenance | Detailed but awaiting task split: [built-in snapshot guide](guides/snapshots.md), [package README](../src/XBullet.EasyTesting.Snapshots/README.md) | `SnapshotAssertTests`, `BuiltInSnapshotTests`, `ControllerSnapshotTests`, `JsonSnapshotExamples` | Split by task, establish canonical pages, document package boundaries and migration | M6 |
| `XBullet.EasyTesting.Snapshots.Http` | Snapshots for stub requests and complete outbound exchanges, request/response options | Partial: [package README](../src/XBullet.EasyTesting.Snapshots.Http/README.md) | `SnapshotAssertTests`, `ExternalApiControllerTests`, `InteroperabilityTests` | Focused request/exchange guide, redaction, formats, defaults, interoperation | M6 |
| `XBullet.EasyTesting.Snapshots` | Compatibility facade forwarding snapshot core and HTTP APIs | Minimal: [package README](../src/XBullet.EasyTesting.Snapshots.Compatibility/README.md) | `InteroperabilityTests`, type-forward baselines | Migration guidance and a precise statement of compatibility guarantees | M6 |
| `XBullet.EasyTesting.Verify.Xunit` | Verify.Xunit v3 controller-response snapshot adapter | Partial: [detailed guide](guides/verify-xunit.md), [package README](../src/XBullet.EasyTesting.Verify.Xunit/README.md) | `ControllerSnapshotTests`, `CrudControllerSnapshotTests`, `InteroperabilityTests` | Decision guide versus built-in snapshots, recorder behavior, configuration and migration | M6 |

Test names in this table refer to files under `tests/`. The executable-example milestone will turn
the most readable scenarios into stable links and source regions.

## Cross-cutting concept coverage

| Concept | Current source | Status | Needed canonical page | Target |
| --- | --- | --- | --- | --- |
| Choose packages | Root package list | Partial | `getting-started/choosing-packages.md` | M1 |
| First authenticated controller test | Root, core package, and authentication-guide examples | Partial | `getting-started/first-controller-test.md` | M3 |
| Host selection | Authentication and scenarios guide | Partial | `concepts/test-hosts.md` | M3 |
| Scenario lifecycle and isolation | Authentication and scenarios guide | Partial | `concepts/scenarios-and-isolation.md` | M3 |
| Authentication models | Authentication and scenarios guide | Partial | `concepts/authentication.md` plus focused recipes | M3 |
| Resources, ownership, and cleanup | Authentication guide and XML comments | Minimal | `concepts/resources-and-cleanup.md` | M3 |
| Failure diagnostics | Individual packages and tests | Partial | Package guide sections plus a troubleshooting index | M3-M6 |
| Security and redaction | Snapshot material and scattered notes | Partial | Shared safety guidance linked from relevant guides | M4-M6 |
| Multi-target and parallel behavior | Snapshot README and tests | Partial | Relevant concept and guide sections | M3-M6 |
| Built-in versus Verify snapshots | Root and Verify package README | Partial | `guides/snapshots/choose-an-assertion-engine.md` | M6 |
| API reference | XML documentation files | Missing as a browsable resource | Generated reference site | M7 |

## Duplication and consistency findings

Milestone 1 removed duplication from the root entry point and replaced generic repository links.
The remaining guide/package overlap is tracked for the feature-focused milestones. NuGet READMEs
must retain enough standalone content to be useful on package pages.

| Area | Finding | Canonical resolution | Status |
| --- | --- | --- | --- |
| Authentication | Basic, multi-scheme, hybrid, and end-to-end flows previously made the root README difficult to navigate | Keep one short root example, use concept and recipe pages for modes, retain only the package minimum in the NuGet README | Root resolved in M1; guide split remains in M3 |
| EF Core | Provider choice and database lifecycle appear in both the extracted guide and package README | Make the detailed EF guide canonical; package README summarizes provider choice and links to it | Planned for M3 |
| HTTP and messaging | Examples need focused recipes beyond their package-level minimums | Use the application-boundary guide as the current entry point and create focused package recipes | Navigation resolved in M1; recipes remain in M4 |
| Azure Functions | Package and detailed guides overlap while trigger behavior primarily lives in tests | Make trigger-specific guides canonical and keep one package-level example | Navigation resolved in M1; guide split remains in M5 |
| Snapshots | The extracted guide and core package README still contain substantial overlapping workflows | Split detailed workflows into canonical snapshot guides; keep the package page curated for NuGet | Planned for M6 |
| Verify.Xunit | The extracted guide and package page repeat some setup and recorder behavior | Keep package setup in the package README and comparison/workflow detail in snapshot guides | Planned for M6 |
| Package naming | The source directory is `XBullet.EasyTesting.Snapshots`, while the lightweight package is `XBullet.EasyTesting.Snapshots.Core` and a separate project provides the compatibility facade | Always use exact package IDs in prose and explain the split on one migration page | Cross-links resolved in M1; migration page remains in M6 |
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

Use the full checklist in [the roadmap](documentation-roadmap.md#feature-page-definition-of-done)
and the review checklist in the [style guide](documentation-style-guide.md#review-checklist). At a
minimum, a coverage row moves to **Detailed** only when it has:

- A discoverable, focused guide.
- A verified minimal example and a realistic example.
- Documented defaults, lifecycle, diagnostics, and limitations.
- Links to related concepts and public API reference.
- Passing automated documentation checks once those checks are introduced.
