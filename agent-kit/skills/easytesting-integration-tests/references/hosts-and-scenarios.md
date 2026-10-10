# Hosts and scenarios

## Minimal fixture shape

The following xUnit v3 example assumes the application exposes a public `Program` and an endpoint
at `/api/orders` requiring the `Administrator` role. Adapt those application-specific details and
assertions to the actual endpoint; retain the consuming project's runner.

```csharp
using System.Net;
using XBullet.EasyTesting.Hosting;
using Xunit;

public sealed class OrdersTests
    : IClassFixture<AuthenticatedWebApplicationFactory<Program>>
{
    private readonly AuthenticatedWebApplicationFactory<Program> _factory;

    public OrdersTests(AuthenticatedWebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Administrator_can_read_orders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var client = scope.Client()
            .AsUser(user => user
                .WithNameIdentifier("user-42")
                .WithRole("Administrator"))
            .WithoutRedirects()
            .Build();

        using var response = await client.GetAsync("/api/orders", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

Add response and effect assertions appropriate to the contract. `EnsureSuccessStatusCode()` alone
cannot distinguish the expected success status or establish the body contract.

## Scope and service ownership

`CreateTestScenarioScopeAsync` creates a child host and holds the factory's scenario gate until
disposal. Use scope-owned clients and services to observe scenario configuration. Do not acquire a
second scope on the same factory while the first scope is held and then wait for it inside the
first scope: the gate serializes those lifetimes.

Use stable factory-wide overrides for immutable fakes and common configuration. Use the scope
configuration callback for per-test replacements and values. Remove existing service registrations
when a replacement must be deterministic. Use `scope.Services` with appropriate DI lifetimes;
do not keep a scoped `DbContext` in a singleton fixture.

Shared stubs, recorders, and other mutable resources need the framework's scenario-resource
lifecycle. Inspect the installed resource APIs and the project's factory before registering one.
The factory gate does not isolate resources shared across unrelated factories or an external
database used by independent test processes.

## Fluent scenarios

`scope.Scenario()` supports arrangements, one authentication profile, request configuration, and
exactly one request before `ExecuteAsync`. Execute a builder once and dispose its result. For a
multi-request CRUD workflow, use a scope-owned client or a fresh builder for each request while
holding the same scenario scope.

`PostJson` and `PutJson` use hosted System.Text.Json options and accept supported per-request
overrides. Do not assume these helpers use Newtonsoft.Json just because MVC response serialization
does. To send a Newtonsoft-specific contract, serialize it with the intended settings and provide
JSON `HttpContent` through a supported request API. Preserve the application's serializer.

## Choose a different host deliberately

Use the Startup-based factory when testing a Startup pipeline without running `Program.Main`.
Use the EF factory when database setup and isolation are needed. For Functions or Aspire, use
their dedicated packages and host documentation; an ASP.NET Core factory does not emulate those
hosting models.

- [Scenario lifetime and isolation](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/concepts/scenarios-and-isolation.md)
- [Service and configuration overrides](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/service-and-configuration-overrides.md)
- [Host selection](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/concepts/test-hosts.md)
