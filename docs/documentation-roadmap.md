# Documentation roadmap

This roadmap turns the existing README files, XML comments, public API baselines, and integration
tests into documentation that is easy to discover, learn from, and keep correct. Milestones are
ordered by dependency rather than calendar date. Each milestone should be small enough to review
and merge independently.

## Outcomes

- Help a new user run a useful integration test in less than 15 minutes.
- Explain both the happy path and important failure modes for every major capability.
- Provide realistic, compilable examples for every package.
- Make every supported public API discoverable without turning the root README into a reference
  manual.
- Detect broken examples, links, and generated documentation in CI.

## Documentation layers

The documentation should have four distinct layers:

1. The root README introduces the project, helps users choose packages, and provides one minimal
   end-to-end example.
2. Concept pages explain shared ideas such as hosts, scenarios, authentication, resources, and
   isolation.
3. Task-oriented guides show how to accomplish a specific testing goal.
4. Generated API pages document individual namespaces, types, members, and configuration options.

Package READMEs remain useful on NuGet. They should contain an installation command, a minimal
example, key limitations, and links to the relevant detailed guides and API reference.

## Milestone 0: documentation contract and inventory

**Goal:** agree on what complete documentation means and establish a measurable baseline.

**Status:** complete. See the [coverage baseline](documentation-coverage.md),
[style guide](documentation-style-guide.md), and [feature-guide template](feature-guide-template.md).

- [x] Inventory features by package, using `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`, source
  XML comments, and tests.
- [x] Create a coverage matrix mapping each feature to its package README, detailed guide, API
  reference, executable example, and troubleshooting coverage.
- [x] Add a documentation style guide covering terminology, headings, code style, links, and the
  distinction between tutorials, concepts, recipes, and reference material.
- [x] Define the standard feature-page template.
- [x] Identify duplicate or contradictory guidance in the root and package READMEs.
- [x] Record intentional limitations that must be stated consistently across documents.

**Exit criteria**

- Every package and major public capability appears in the coverage matrix.
- Each planned page has an owner, source material, and target milestone.
- Reviewers have one checklist for deciding whether a feature is documented.

## Milestone 1: navigation and project entry points

**Goal:** let readers quickly understand the project and find the correct package or guide.

**Status:** complete. The [documentation home](index.md) provides goal-oriented navigation, the
root README is a concise entry point, and package READMEs use consistent installation, example,
behavior, and documentation sections.

- [x] Add `docs/index.md` as the documentation home.
- [x] Add a package-selection table organized by testing problem rather than assembly name alone.
- [x] Add a task-oriented navigation page linking common goals to the appropriate guides.
- [x] Reduce the root README to project positioning, installation, package selection, one complete
  example, and links to deeper material.
- [x] Standardize package README structure and cross-links.
- [x] Add clear navigation between related packages, especially snapshot core, snapshot HTTP
  adapters, the compatibility facade, and Verify.Xunit.

**Exit criteria**

- A reader can reach any package guide from the root README in at most two links.
- The root README has one primary getting-started path and does not duplicate advanced guides.
- Every NuGet package README links to detailed documentation and related packages.

## Milestone 2: executable example foundation

**Goal:** make documentation examples readable and continuously verifiable.

**Status:** foundation complete. New canonical examples live in the multi-target integration-test
projects, named source regions feed generated Markdown blocks, and CI rejects stale snippets.
Existing package examples will adopt the mechanism as their focused guides are revised in
Milestones 3 through 6.

- [x] Decide where canonical examples live: a dedicated examples test project or clearly marked
  documentation scenarios in the existing integration-test projects.
- [x] Reuse existing sample applications and fixtures where they are clear; create smaller ones only
  when unrelated setup would obscure the documented behavior.
- [x] Define named source regions and a single-source synchronization mechanism for Markdown examples.
- [x] Add a minimal example, a realistic example, and a failure example pattern.
- [x] Compile and run all canonical examples on supported target frameworks in CI.
- [x] Document how contributors add or update an example.

**Exit criteria**

- New and migrated canonical examples do not depend on untested, manually duplicated code samples.
- At least one example demonstrates the complete arrange, act, assert, and cleanup lifecycle.
- A deliberately broken example causes the documentation validation job to fail.

## Milestone 3: core testing journey

**Goal:** completely document the path from an empty test project to maintainable authenticated
ASP.NET Core integration tests.

**Status:** complete. Core concepts and task guides now cover host selection, authentication,
scenario isolation, resources, overrides, response assertions, and EF Core with executable examples.

- [x] Write the first-controller-test tutorial.
- [x] Explain `AuthenticatedWebApplicationFactory`, `StartupAuthenticatedWebApplicationFactory`,
  and the composable `EasyTestHost` path, including when to select each.
- [x] Document anonymous users, custom users, claims, roles, multiple schemes, API keys, Azure AD
  identities, certificates, JWTs, and end-to-end authentication.
- [x] Explain scenario scopes, per-test isolation, environment resources, cleanup, cancellation, and
  terminal diagnostics.
- [x] Document HTTP response assertions, including representative failure output.
- [x] Document service replacement and configuration overrides.
- [x] Write Entity Framework Core recipes for in-memory, SQLite, production-like providers,
  seeding, querying, migrations, database recreation, and cleanup failures.

**Exit criteria**

- A new user can build and understand a complete authenticated CRUD test by following the guides.
- Every major core host and authentication mode has a tested example.
- Database documentation clearly explains when the EF Core in-memory provider is insufficient.

## Milestone 4: application boundary testing

**Goal:** document how tests observe or replace the application's external interactions.

**Status:** complete. Focused guides cover [outbound HTTP](guides/outbound-http.md),
[messaging](guides/messaging.md), [observability](guides/observability.md), and
[Azure SDK clients](guides/azure-sdk.md), with the
[application-boundary hub](guides/application-boundaries.md) explaining combined scenarios.

- [x] Document outbound HTTP request matching by method, path, query, headers, text, JSON, and
  custom predicates.
- [x] Cover response sequences, delays, cancellation, timeouts, malformed responses, faults, call
  recording, and verification diagnostics.
- [x] Document message recording for Kafka, Azure Service Bus, Azure Notification Hubs, and custom
  transports.
- [x] Show assertions for message destinations, headers, typed payloads, counts, and ordering.
- [x] Document structured log, activity, and metric capture with realistic assertions.
- [x] Explain deterministic time and observability cleanup.
- [x] Document Azure SDK responses, paging, credentials, dependency injection, pipeline transport,
  request recording, and failure verification.

**Exit criteria**

- HTTP, messaging, observability, and Azure packages each have a minimal and realistic example.
- Each guide includes at least one negative assertion or diagnostic example.
- Cross-package scenarios explain how to combine boundary tools within one test scope.

## Milestone 5: infrastructure and distributed applications

**Goal:** document tests that require real infrastructure, multiple processes, or Azure Functions.

**Status:** complete. The [container infrastructure guide](guides/testcontainers.md),
[Aspire guide](guides/aspire.md), [Azure Functions host guide](guides/azure-functions.md),
[trigger recipes](guides/azure-functions-triggers.md), and
[Durable boundary](guides/azure-functions-durable.md) use executable examples for supported
infrastructure and invocation behavior.

- [x] Document built-in PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, and Service Bus
  emulator Testcontainers modules.
- [x] Show arbitrary-container configuration, readiness, resource sharing, cleanup, and failure
  diagnostics.
- [x] Document Aspire application startup, resource discovery, endpoint access, readiness waits,
  logs, diagnostics, and shutdown.
- [x] Document the Azure Functions test host and direct function invocation.
- [x] Add trigger-specific recipes for HTTP, timer, Kafka, Service Bus, Queue Storage, Blob, Event
  Grid, and Event Hubs.
- [x] Document output bindings, middleware, retry state, function contexts, and invocation results.
- [x] Explain supported Durable activity dispatch and unsupported Durable runtime behavior.

**Exit criteria**

- Each infrastructure module has a working setup and cleanup example.
- Aspire and Azure Functions guides include troubleshooting sections with representative errors.
- Durable Functions documentation clearly distinguishes emulation from the real runtime.

## Milestone 6: snapshot workflow consolidation

**Goal:** provide one coherent snapshot-testing story across core, HTTP adapters, the compatibility
facade, and Verify.Xunit.

**Status:** complete. The [snapshot workflow](guides/snapshots.md) now routes users from package
selection through executable recipes, stabilization, review, and maintenance without requiring
knowledge of the package's implementation history.

- [x] Split the existing detailed material into [getting started](guides/snapshots/getting-started.md),
  concepts, [recipes](guides/snapshots/recipes.md),
  [maintenance](guides/snapshots/maintenance.md), and reference-oriented pages.
- [x] Explain snapshot [file locations, naming, variants, and parallel
  execution](guides/snapshots/files-and-naming.md).
- [x] Document JSON, text, controller response, complete HTTP exchange, and outbound stub snapshots
  in the [recipe collection](guides/snapshots/recipes.md).
- [x] Cover scrubbing, redaction, ignoring, hashing, canonicalization, and sorting with before/after
  examples in [stabilizing snapshot data](guides/snapshots/stabilizing-data.md).
- [x] Document mismatch diagnostics, diff tools, acceptance, guarded updates, obsolete-file
  detection, and CI behavior in [snapshot maintenance](guides/snapshots/maintenance.md).
- [x] Explain project-wide and reusable defaults, including precedence rules, in [snapshot
  defaults](guides/snapshots/defaults.md).
- [x] Add a [migration guide](guides/snapshots/migration.md) for the package split and compatibility
  facade.
- [x] Document when to use [built-in assertions versus
  Verify.Xunit](guides/snapshots/choose-engine.md).

**Exit criteria**

- Users can select the correct snapshot package without understanding internal package history.
- All maintenance commands and update modes include safety notes and CI guidance.
- Snapshot examples cover sensitive-data redaction and a mismatch review workflow.

## Milestone 7: generated API reference

**Goal:** make the existing XML documentation available as searchable public reference material.

**Status:** complete. The [searchable API reference](api/index.md) is generated by repository-pinned
Docfx from the validated public Release surface, checked in CI with warnings treated as errors, and
published from `main` and release tags.

- [x] Select and configure a .NET-compatible documentation generator.
- [x] Generate pages for every shipped public namespace, type, member, parameter, return value, and
  exception where applicable.
- [x] Group reference navigation by package and namespace.
- [x] Add conceptual links from important entry-point APIs to the relevant guides.
- [x] Resolve missing or unclear XML comments discovered by reference generation.
- [x] Exclude compatibility-only implementation details while documenting supported facade usage.
- [x] Publish a release-aligned documentation site through GitHub Pages automation.

**Exit criteria**

- Every shipped public API is present in the generated reference or explicitly excluded with a
  reason.
- Reference generation completes without documentation warnings selected by the project.
- Package READMEs and detailed guides link to stable API pages.

## Milestone 8: documentation quality gates and maintenance

**Goal:** make documentation quality part of the normal development and release process.

**Status:** complete. The consolidated documentation verifier now gates pull requests, publication,
and package releases. The [maintenance policy](contributing/documentation-maintenance.md) defines
link-failure handling, ownership, review intervals, and coverage responsibilities.

- [x] Add Markdown formatting and style validation.
- [x] Add internal and external link checking with an explicit policy for transient external
  failures.
- [x] Validate generated snippets and API pages in CI.
- [x] Add a pull-request checklist requiring documentation for public behavior changes.
- [x] Add a [release checklist](contributing/release-checklist.md) for versioned documentation,
  migration notes, and changelog links.
- [x] Add periodic review metadata or a documented review schedule for high-change pages.
- [x] Track documentation coverage in the feature matrix.

**Exit criteria**

- Pull requests cannot merge with broken internal links, invalid snippets, or stale generated
  documentation.
- New public functionality must update the coverage matrix or explicitly state why no user-facing
  documentation is required.
- Each release links its changes to relevant guides, migration notes, and API reference pages.

## Feature-page definition of done

A feature is documented when all applicable items below are complete:

- [ ] The page states the problem, intended audience, and when to use the feature.
- [ ] Required package references, prerequisites, and namespaces are explicit.
- [ ] The minimal example compiles and runs.
- [ ] A realistic example demonstrates normal application structure.
- [ ] Configuration options and default behavior are explained.
- [ ] Lifecycle, concurrency, cancellation, and cleanup behavior are covered where relevant.
- [ ] At least one failure mode includes expected diagnostics and a corrective action.
- [ ] Limitations, security considerations, and alternatives are stated.
- [ ] Related guides, APIs, and canonical tests are linked.
- [ ] The page passes automated documentation checks.

## Delivery status

Milestones 0 through 8 are complete. Future documentation work follows the feature definition of
done above, the [coverage matrix](documentation-coverage.md), and the
[maintenance schedule](contributing/documentation-maintenance.md).
