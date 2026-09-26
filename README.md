# XBullet.EasyTesting

[![CI](https://github.com/olgerd007/XBullet.EasyTesting/actions/workflows/ci.yml/badge.svg)](https://github.com/olgerd007/XBullet.EasyTesting/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/XBullet.EasyTesting.svg)](https://www.nuget.org/packages/XBullet.EasyTesting)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Reusable infrastructure for integration testing authenticated ASP.NET Core applications through
an in-memory `TestServer`. Extension packages cover persistence, outbound HTTP, messaging,
observability, real containers, Aspire, Azure SDK clients, Azure Functions, and snapshots.

All packages target .NET 8, .NET 9, and .NET 10.

## Get started

Install the core package in the test project:

```shell
dotnet add package XBullet.EasyTesting
```

Make the application entry point visible to the test project, create an authenticated factory,
and build a client with the identity required by the endpoint:

```csharp
public partial class Program;

public sealed class OrdersControllerTests
    : IClassFixture<AuthenticatedWebApplicationFactory<Program>>
{
    private readonly AuthenticatedWebApplicationFactory<Program> _factory;

    public OrdersControllerTests(
        AuthenticatedWebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Administrator_can_get_orders()
    {
        using var client = _factory.Client()
            .AsUser(user => user
                .WithNameIdentifier("user-42")
                .WithRole("Administrator")
                .WithClaim("permission", "orders.read"))
            .WithoutRedirects()
            .Build();

        using var response = await client.GetAsync("/api/orders");

        response.EnsureSuccessStatusCode();
    }
}
```

The fluent client is anonymous until `AsUser` is called. Simulated users exercise application
authorization inside the test host; use the end-to-end authentication workflow when a test must
also validate production credential parsing.

Continue with the [authentication and scenarios guide](docs/guides/authentication-and-scenarios.md)
for anonymous requests, scenario isolation, multiple schemes, API keys, Azure AD identities,
certificates, JWTs, composable hosts, and end-to-end authentication.

## Choose packages

Install only the packages required by a test project:

| Testing goal | Package |
| --- | --- |
| Host an ASP.NET Core application and simulate identities | `XBullet.EasyTesting` |
| Seed, query, and isolate an EF Core database | `XBullet.EasyTesting.EntityFrameworkCore` |
| Stub and record outbound HTTP calls | `XBullet.EasyTesting.Http` |
| Record published messages | `XBullet.EasyTesting.Messaging` |
| Capture logs, traces, metrics, and deterministic time | `XBullet.EasyTesting.Observability` |
| Create Azure SDK responses, credentials, paging, and transports | `XBullet.EasyTesting.Azure` |
| Run real dependencies in containers | `XBullet.EasyTesting.Testcontainers` |
| Test an Aspire distributed application | `XBullet.EasyTesting.Aspire` |
| Test .NET isolated Azure Functions | `XBullet.EasyTesting.AzureFunctions` |
| Use framework-independent built-in snapshots | `XBullet.EasyTesting.Snapshots.Core` |
| Snapshot requests captured by outbound HTTP stubs | `XBullet.EasyTesting.Snapshots.Http` |
| Keep compatibility with the original combined snapshot package | `XBullet.EasyTesting.Snapshots` |
| Use Verify.Xunit v3 for controller snapshots | `XBullet.EasyTesting.Verify.Xunit` |

The [documentation home](docs/index.md#choose-packages) explains package boundaries, runtime
prerequisites, common combinations, and the recommended package for each testing goal.

## Guides

- [Authentication, test hosts, scenarios, and isolation](docs/guides/authentication-and-scenarios.md)
- [External APIs, database persistence, and messaging](docs/guides/application-boundaries.md)
- [Azure SDK testing](src/XBullet.EasyTesting.Azure/README.md)
- [Observability](src/XBullet.EasyTesting.Observability/README.md)
- [Testcontainers](src/XBullet.EasyTesting.Testcontainers/README.md)
- [Aspire distributed applications](src/XBullet.EasyTesting.Aspire/README.md)
- [.NET isolated Azure Functions](docs/guides/azure-functions.md)
- [Built-in snapshot testing](docs/guides/snapshots.md)
- [Verify.Xunit controller snapshots](docs/guides/verify-xunit.md)

The [task-oriented documentation index](docs/index.md#find-documentation-by-goal) provides the
complete navigation map.

## Important boundaries

- Simulated identities validate authorization behavior, not production credential validation.
- The EF Core in-memory provider does not reproduce relational constraints or transactions.
- Testcontainers requires a Docker-compatible runtime; Service Bus emulator scenarios also
  require explicit license acceptance.
- Durable Functions support emulates `CallActivityAsync`, not timers, external events,
  sub-orchestrators, or replay behavior.
- Snapshot and diagnostic output can contain sensitive application data. Configure exclusions or
  redaction before committing files or publishing logs.

See the maintained [limitations register](docs/documentation-coverage.md#limitations-register) for
package-specific constraints.

## Examples

Executable scenarios live in:

- [`tests/TestApi.IntegrationTests`](tests/TestApi.IntegrationTests) for ASP.NET Core,
  authentication, persistence, external boundaries, observability, containers, and snapshots.
- [`tests/TestStartupApi.IntegrationTests`](tests/TestStartupApi.IntegrationTests) for
  `Startup`-based applications and composed business scenarios.
- [`tests/TestFunctions.IntegrationTests`](tests/TestFunctions.IntegrationTests) for Azure
  Functions hosts, triggers, bindings, middleware, and Durable activity dispatch.
- [`tests/XBullet.EasyTesting.Tests`](tests/XBullet.EasyTesting.Tests) for lower-level package
  behavior.

## Contributing and releases

See the [contribution guide](CONTRIBUTING.md), [build and release guide](docs/contributing/building-and-releasing.md),
[changelog](CHANGELOG.md), and [security policy](SECURITY.md).
