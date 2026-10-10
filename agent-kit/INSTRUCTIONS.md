# XBullet.EasyTesting instructions

Apply these rules when writing, reviewing, or repairing tests that use XBullet.EasyTesting in any
consuming application. Preserve the application's architecture, test runner, target frameworks,
package management, authentication handlers, and JSON serializer. Explicit project requirements
take precedence. These instructions do not require adopting XBullet for unrelated unit tests.

## Discover the project first

- Inspect the test project, application entry point, existing factories, fixtures, and package
  versions. Reuse working conventions before introducing another host or test abstraction.
- Verify APIs against the installed package version. Bundled skills describe current framework
  behavior; if an API is unavailable, use a supported equivalent or explain a necessary upgrade.
- Keep framework dependencies and test doubles in test projects. Exercise application behavior
  through HTTP; replace external boundaries through test-host dependency injection.

## Select the smallest package set

| Need | Package |
| --- | --- |
| ASP.NET Core host, simulated identities, scenario scopes | `XBullet.EasyTesting` |
| EF Core setup and database isolation | `XBullet.EasyTesting.EntityFrameworkCore` |
| Outbound HTTP stubs and recorded calls | `XBullet.EasyTesting.Http` |
| Published-message recording | `XBullet.EasyTesting.Messaging` |
| Logs, traces, metrics, deterministic time | `XBullet.EasyTesting.Observability` |
| Azure SDK test responses and transports | `XBullet.EasyTesting.Azure` |
| Real container dependencies | `XBullet.EasyTesting.Testcontainers` |
| Aspire application testing | `XBullet.EasyTesting.Aspire` |
| .NET isolated Functions testing | `XBullet.EasyTesting.AzureFunctions` |
| Built-in object, body, controller, and HTTP exchange snapshots | `XBullet.EasyTesting.Snapshots.Core` |
| HTTP stub snapshots and fluent `SnapshotScenario` adapter | `XBullet.EasyTesting.Snapshots.Http` |
| Existing combined snapshot facade | `XBullet.EasyTesting.Snapshots` |
| Verify.Xunit v3 integration | `XBullet.EasyTesting.Verify.Xunit` |

## Preserve meaningful test boundaries

- Use `AuthenticatedWebApplicationFactory<TEntryPoint>` or an existing derived factory. Expose
  `Program` to the test assembly when necessary. Use a scenario scope for mutable test state;
  dispose it with `await using`. Dispose clients, responses, and scenario results.
- Fluent clients are anonymous until an identity is selected. `AsUser` tests authorization using
  simulated identities; it does not prove production credential validation. Test actual credential
  handlers separately. Preserve named authentication schemes and expected challenge behavior.
- Isolate databases, stubs, message recorders, and telemetry. Register shared mutable doubles as
  scenario resources rather than allowing expectations or recorded data to leak across tests.
- Propagate a meaningful cancellation token. Wait for asynchronous outcomes with bounded polling
  or a completion signal. Keep side effects outside retrying assertion callbacks.
- Assert status codes, response contracts, durable state, and relevant outbound effects. Cover
  applicable unauthenticated, forbidden, invalid-input, and failure cases without weakening policies.
- Do not use EF Core in-memory tests as evidence of relational constraints or provider-specific SQL.

## Keep snapshot contracts honest

- Prefer HTTP body or exchange snapshots for the application's wire JSON contract. They parse the
  actual HTTP content, including Newtonsoft.Json output; they do not reserialize the application
  DTO. Direct object snapshots use System.Text.Json and do not inherit Newtonsoft attributes.
- Capture exchange request bodies before transport with a recorder when they matter. Configure
  recorder capture options before sending; use snapshot settings for assertion-time transformations.
  Consume streaming response content before expecting a recorded body instead of `{NotRead}`.
- Redact secrets and scrub only volatile values outside the asserted contract. Review snapshot
  changes before accepting them; keep normal CI in verification mode. Commit reviewed verified
  files, never received files. Do not enable blanket updates just to make failures pass.

## Use focused skills and verify

Load only the relevant installed skill: `easytesting-integration-tests` for hosts and identities,
`easytesting-application-boundaries` for persistence and external effects, or
`easytesting-snapshots` for snapshot contracts. Each skill has portable, relative references.

Run the consuming project's restore, build, relevant tests, and configured format checks. Inspect
`global.json` and the test project before choosing VSTest or Microsoft.Testing.Platform filtering.
Report actual commands, outcomes, and any environment limitations. Never claim unexecuted tests
passed, hide failures by disabling tests, or replace missing APIs with invented ones.
