# Documentation maintenance

Documentation is maintained with the same review discipline as source code. Pull requests run the
deterministic checks; a scheduled workflow checks external sites without making transient network
failures block ordinary contributions.

## Quality gates

| Check | Local solution build | Pull requests | Scheduled | Failure policy |
| --- | --- | --- | --- | --- |
| Markdown structure and formatting | Required | Required | No | Any violation fails |
| Executable snippet synchronization | Required | Required | No | Any stale snippet fails |
| Internal files and anchors | Required | Required | Yes | Any missing target fails |
| XML comments and generated API pages | Required | Required | No | Any selected Docfx warning fails |
| External links | No | No | Weekly and manual | Permanent failures fail; transient failures warn after retries |

An ordinary local solution build runs the pull-request-equivalent checks once, after its library
projects finish. During a source-only iteration, pass `-p:VerifyDocumentationOnBuild=false` to opt
out temporarily. Run an ordinary build again before opening a pull request.

To run the documentation gate directly after a Release build:

```powershell
./eng/verify-documentation.ps1 -NoBuild -WarningsAsErrors
```

Run the broader external check when changing destinations or preparing a release:

```powershell
./eng/verify-documentation-links.ps1 -CheckExternalLinks -TransientFailurePolicy Warn
```

External `404` and `410` responses, and other non-transient client errors, fail immediately.
Timeouts, rate limits, and server errors are retried with bounded backoff. They become warnings by
default because a third-party outage is not evidence that repository content is wrong. Pass
`-TransientFailurePolicy Error` during a deliberate strict audit.

The project's own GitHub Pages URLs are excluded from network probing because the same workflow
validates those generated paths locally before deployment. Every excluded pattern must include a
documented reason here and remain as narrow as possible.

## Review schedule

| Area | Responsible reviewers | Cadence | Additional trigger | Last full review | Next review by |
| --- | --- | --- | --- | --- | --- |
| Entry points and package selection | Repository maintainers | Quarterly | Package added, renamed, or split | 2026-09-27 | 2026-12-31 |
| Core hosts, authentication, and scenarios | Core-package maintainers | Quarterly | Public hosting or authentication change | 2026-09-27 | 2026-12-31 |
| HTTP, messaging, Azure, and observability boundaries | Owning package maintainers | Quarterly | New matcher, transport, collector, or SDK helper | 2026-09-27 | 2026-12-31 |
| Testcontainers, Aspire, and Azure Functions | Infrastructure maintainers | Quarterly | Runtime, image, trigger, or framework support change | 2026-09-27 | 2026-12-31 |
| Snapshot workflows and safety guidance | Snapshot maintainers | Every release | Update, redaction, naming, or package change | 2026-09-27 | Next release |
| API reference and package landing pages | Public-API owner | Every release | Public API approval changes | 2026-09-27 | Next release |
| Security, limitations, and migration guidance | Release maintainer | Every release | Security or breaking behavior change | 2026-09-27 | Next release |

A review confirms examples still compile, defaults and limitations remain accurate, external
requirements are current, links reach the intended material, and sensitive-data guidance matches
the implementation. Update the dates in this table in the pull request that records a full review.

## Coverage ownership

Every public feature belongs in `docs/documentation-coverage.md`. A public behavior change must do
one of the following:

1. Update the existing feature or package row and its canonical documentation.
2. Add a row for a new capability and identify the milestone or maintenance owner.
3. State in the pull-request summary why the change has no user-facing documentation impact.

Do not mark an area `Detailed` merely because an API page exists. Detailed coverage also requires a
task-oriented path, realistic example, limitations, diagnostics, and related API links.
