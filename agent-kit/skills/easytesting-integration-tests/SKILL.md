---
name: easytesting-integration-tests
description: >-
  Write or repair ASP.NET Core integration tests using XBullet.EasyTesting hosts, authentication,
  scenario isolation, and response assertions in a consuming application. Use for framework setup
  and endpoint tests; ordinary domain unit tests do not need this skill.
---

# XBullet integration tests

## Understand the contract

Inspect the consuming application's test runner, target frameworks, installed XBullet versions,
entry point, endpoint routing, authentication policies, and existing factory. Determine which
observable behavior the requested test must establish. Preserve production behavior and reuse the
project's fixture conventions; do not install a different runner just to match an example.

## Choose the host and lifetime

Use the core `XBullet.EasyTesting` package and import `XBullet.EasyTesting.Hosting`.
Prefer an existing factory, otherwise `AuthenticatedWebApplicationFactory<TEntryPoint>` or
`EasyTestHost.Create<TEntryPoint>()`. A top-level ASP.NET Core entry point may need
`public partial class Program;` in the application project.

Read [hosts and scenarios](references/hosts-and-scenarios.md) for fixture structure, configuration,
service overrides, scoped state, and fluent requests. Keep test doubles in the test assembly.

## Select the authentication model

- Use `.Client().AsUser(...).Build()` to test authorization and business behavior. Supply the
  actual roles and claims required by the policy. A client without a profile is anonymous.
- Use `.WithoutRedirects()` when asserting the original challenge or forbidden response.
- For named schemes, configure the exact scheme in the factory's `ConfigureTestAuthentication`.
  Do not change an endpoint policy to accommodate a test identity.
- Simulated users, API-key profiles, and Azure AD-shaped profiles do not validate real credentials.
  For token signatures, expiry, key parsing, or certificate validation, use the framework's
  end-to-end helpers and the application's actual handler. Inspect installed APIs before choosing
  a helper; never assume an `As...` simulated profile proves credential parsing.

## Arrange, act, and assert

Use a fresh `TestScenarioScope` for mutable state, and hold it for the whole test. Arrange required
data and external responses; send the request through TestServer. Assert the expected status and
public response shape, then any durable state and relevant external effects. Add applicable negative
authorization and validation cases. Test middleware and routing through HTTP rather than invoking
controllers directly when HTTP behavior is part of the requirement.

Use the existing runner's cancellation token where available. `TestContext.Current.CancellationToken`
is an xUnit v3 convention, not a requirement for NUnit, MSTest, or xUnit v2. Dispose scenario scopes
asynchronously and dispose clients, responses, and results according to ownership.

## Validate and diagnose

Run relevant tests with the project's actual runner. Before broad changes, diagnose whether a
failure is an assertion mismatch, host startup problem, scheme mismatch, unarranged boundary, or
state leakage. Preserve failure diagnostics and avoid weakening policies or disabling parallelism
to conceal an isolation bug.

For persistence or remote effects, load `easytesting-application-boundaries` if installed. For
snapshot contracts, load `easytesting-snapshots` if installed. Both are optional; use the installed
package's documentation when those skills are unavailable.

## Authoritative references

- [First controller test](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/getting-started/first-controller-test.md)
- [Authentication models](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/concepts/authentication.md)
- [Response assertions](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/response-assertions.md)

Use documentation matching the installed release where available; `main` may contain newer APIs.
