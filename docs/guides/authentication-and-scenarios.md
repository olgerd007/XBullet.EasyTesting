# Authentication and scenarios

Use this page as the navigation hub for ASP.NET Core test hosts, authentication, scenario
isolation, configuration, services, response assertions, and EF Core. The detailed material is
split by concept and task so each page has one purpose.

## Start with a working test

Follow [run your first controller test](../getting-started/first-controller-test.md) to create an
authenticated request, arrange database state, assert a response, and inspect a failure. Its code
is compiled and executed on every supported target framework.

## Choose the right concept

| Question | Read |
| --- | --- |
| Which factory or host builder should I use? | [Choose a test host](../concepts/test-hosts.md) |
| Should this test simulate a user or validate a real credential handler? | [Authentication models](../concepts/authentication.md) |
| How are per-test state, concurrency, diagnostics, and cleanup isolated? | [Scenarios and isolation](../concepts/scenarios-and-isolation.md) |
| Who starts, resets, diagnoses, and disposes external resources? | [Resources and cleanup](../concepts/resources-and-cleanup.md) |

## Follow a task guide

- [Override services and configuration](service-and-configuration-overrides.md)
- [Assert controller responses](response-assertions.md)
- [Test with Entity Framework Core](entity-framework-core.md)
- [Test external APIs and messaging](application-boundaries.md)
- [Use built-in snapshots](snapshots.md)
- [Use Verify.Xunit controller snapshots](verify-xunit.md)

## Authentication paths

Use simulated identities for authorization policies and application behavior. Use end-to-end JWT,
API-key, or certificate helpers when the production authentication handler is itself under test.
The [authentication guide](../concepts/authentication.md) covers general users, roles, claims,
named schemes, Azure AD-shaped principals, federation, multiple identities, hybrid defaults, local
JWT authorities, real API-key injection, certificate validation, and authentication diagnostics.

## Scenario paths

Use a scenario scope whenever a shared factory has mutable database state, HTTP stubs, message
recorders, observability collectors, per-test services, or external resources. The scope holds the
factory's scenario gate for its complete lifetime and captures diagnostics before cleanup when a
test fails.

For a single arrange-and-request operation, use `scope.Scenario()`. It accepts arrangements, one
authentication profile, request configuration, and exactly one request before `ExecuteAsync`.

## Package entry points

- [`XBullet.EasyTesting`](../../src/XBullet.EasyTesting/README.md)
- [`XBullet.EasyTesting.EntityFrameworkCore`](../../src/XBullet.EasyTesting.EntityFrameworkCore/README.md)
- [Core API reference](../api/packages/xbullet-easytesting.md)
- [Entity Framework Core API reference](../api/packages/xbullet-easytesting-entityframeworkcore.md)
- [Complete package selection](../index.md#choose-packages)
