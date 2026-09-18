# Snapshot package roadmap

This roadmap keeps `System.Text.Json` as the canonical serializer and evolves the package in
small, independently releasable milestones.

## Milestone 1: JSON fidelity and content detection

Goal: every JSON entry point produces the same lossless snapshot representation.

- [x] Centralize JSON parsing and document options.
- [x] Preserve number tokens without converting through `double` or `decimal`.
- [x] Recognize `application/json` and media types with the `+json` structured suffix.
- [x] Reject false-positive media types that merely end in `json`.
- [x] Add regression coverage for large integers, high-precision decimals, and exponents.
- [x] Define and cover malformed JSON and duplicate-property behavior.

## Milestone 2: deterministic and parallel-safe files

Goal: snapshot storage remains correct under cancellation, parallel tests, and every supported OS.

- [x] Write snapshots atomically through same-directory temporary files.
- [x] Coordinate concurrent operations targeting the same snapshot.
- [x] Use portable filename sanitization with collision detection.
- [x] Add first-class variants for parameterized tests and multiple snapshots per test.
- [x] Add Windows and Linux path regression coverage.

## Milestone 3: precise structured transformations

Goal: scrub only the intended values without hiding unrelated contract changes.

- [x] Add JSON path or JSON pointer based scrub and ignore rules.
- [x] Add replacement and hashing rules for large fields.
- [x] Add opt-in object-property canonicalization and path-specific array sorting.
- [x] Make date/time scrubbing strict ISO-8601 matching.
- [x] Validate that custom string scrubbers still produce valid JSON.

## Milestone 4: HTTP capture controls and safety

Goal: make request and response snapshots expressive without leaking credentials.

- [x] Add `WithoutHeaders` and configurable header redaction for controller responses.
- [x] Review and expand default sensitive-header exclusions, including `Set-Cookie`.
- [x] Add an `HttpResponseMessage` JSON-body convenience assertion.
- [x] Make body verification reliable after content has already been read.
- [x] Add coverage for empty, text, binary, problem-details, and malformed JSON bodies.

## Milestone 5: diagnostics and approval workflow

Goal: make failures easy to understand and snapshots safe to maintain at scale.

- [x] Report the first structural mismatch as a JSON path with expected and actual values.
- [x] Detect obsolete verified snapshots.
- [x] Add guarded bulk acceptance and removal tooling.
- [x] Require explicit opt-in before updating snapshots in CI.
- [x] Add project-level defaults without mutable global test state.

## Milestone 6: package architecture

Goal: keep the core snapshot package lightweight and framework-independent.

- [x] Move `XBullet.EasyTesting.Http` snapshot adapters into a separate integration package.
- [x] Remove ASP.NET testing dependencies from the core snapshot dependency graph.
- [x] Preserve compatibility through a facade or schedule the split for the next major release.

The lightweight package is `XBullet.EasyTesting.Snapshots.Core`; outbound-request adapters live in
`XBullet.EasyTesting.Snapshots.Http`. The original `XBullet.EasyTesting.Snapshots` package is now a
type-forwarding facade over both packages, preserving existing assembly-qualified type references.

## Milestone 7: test coverage hardening

Goal: protect every public snapshot workflow and its high-risk failure paths with deterministic
tests on every supported target framework.

- [x] Exercise the public HTTP snapshot assertion entry points and stream behavior.
- [x] Cover redaction, request-body parsing, location, acceptance, catalog, and maintenance edges.
- [x] Cover JSON mismatch diagnostics and structured-transformation failure paths.
- [x] Define the operating-system process-launch boundary and exclude only true integration code.
- [x] Enforce at least 90% line and 80% branch coverage for the unit-testable snapshot code in CI.
