# Test a complete order workflow

Build a repeatable integration suite for an order import that authorizes a caller, reads an external
service, saves an order, and publishes an event. This walkthrough uses the repository's
`Startup` application and executable examples, including rejected requests and sequential duplicate
imports. You can read the workflow here without tracing the integration-test suite.

These are production-oriented testing patterns. The sample application keeps orchestration in its
controllers and uses direct event publication; it is not a deployment template or an implementation
of atomic database-and-broker delivery. The final section explains those boundaries.

## Prerequisites and run commands

To run the examples from a repository checkout, install the SDK selected by `global.json` and the
.NET 8, .NET 9, and .NET 10 ASP.NET Core runtimes. Run these commands from the repository root:

```shell
dotnet restore XBullet.EasyTesting.sln
dotnet test tests/TestStartupApi.IntegrationTests/TestStartupApi.IntegrationTests.csproj --framework net8.0 --filter-class TestStartupApi.IntegrationTests.OrderWorkflowDocumentationExamples
dotnet test tests/TestStartupApi.IntegrationTests/TestStartupApi.IntegrationTests.csproj --framework net9.0 --filter-class TestStartupApi.IntegrationTests.OrderWorkflowDocumentationExamples
dotnet test tests/TestStartupApi.IntegrationTests/TestStartupApi.IntegrationTests.csproj --framework net10.0 --filter-class TestStartupApi.IntegrationTests.OrderWorkflowDocumentationExamples
```

Each command runs five cases: successful import, repeated import, anonymous caller, insufficient
scope, and missing upstream order. No Docker, Kafka broker, external API, or Azure account is
needed. The test factory replaces those boundaries before requests execute.

In your own test project, reference your application and install the packages used here:

```shell
dotnet add reference ../MyApi/MyApi.csproj
dotnet add package XBullet.EasyTesting
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
dotnet add package XBullet.EasyTesting.Messaging
dotnet add package xunit.v3
```

Use [the first controller tutorial](../getting-started/first-controller-test.md) for a smaller
`Program`-based application. For this walkthrough, `StartupApiFactory`, `OrderScenario`,
`AsApiUser`, and the feature extensions are test-project types, not library APIs. Their source is
linked below. The generated test snippets omit the containing
`OrderWorkflowDocumentationExamples` class, which derives from
`ScopedTest<Startup, StartupApiFactory>` and implements `IClassFixture<StartupApiFactory>`.
Its constructor passes
`factory, TestContext.Current.CancellationToken` to the base class. The
[complete example file](../../tests/TestStartupApi.IntegrationTests/DocumentationExamples.cs)
includes the class and all required `using` directives.

## Keep the application pipeline and replace its edges

The application endpoint is `POST /api/orders/import/{externalId}`. Its `orders.write` policy,
controller, model binding, feature options, EF operations, and response serialization remain active.
The test factory supplies deterministic replacements:

| Boundary | Application registration | Test replacement | What the test establishes |
| --- | --- | --- | --- |
| Identity | Azure AD JWT bearer handler | Simulated Azure AD identity mapped to `Bearer` | Authorization policy and caller claims |
| External orders | Typed `IExternalOrdersClient` HTTP client | Strict service stub | Application behavior for arranged responses and call counts |
| Persistence | SQLite `OrdersDbContext` | EF Core InMemory database per scenario | Stored values and workflow state |
| Events | `IKafkaPublisher` using Confluent Kafka | Publisher backed by `RecordedMessageBus` | Logical destination, payload, and headers |
| Time | `TimeProvider.System` | Fixed UTC `TimeProvider` | Stable timestamps in responses, storage, and events |

This is the complete factory used by the examples; its `using` directives are in the
[factory source](../../tests/TestStartupApi.IntegrationTests/StartupApiFactory.cs):

<!-- snippet: tests/TestStartupApi.IntegrationTests/StartupApiFactory.cs#docs-order-workflow-factory -->
```csharp
public sealed class StartupApiFactory
    : StartupEntityFrameworkWebApplicationFactory<Startup, OrdersDbContext>
{
    private readonly string _databaseName = $"startup-api-{Guid.NewGuid():N}";

    public StartupApiFactory()
    {
        RegisterScenarioResource("External orders", ExternalOrders);
        RegisterScenarioResource("External customers", ExternalCustomers);
        RegisterScenarioResource("Post provider", PostProvider);
        RegisterScenarioResource("Kafka messages", KafkaMessages);
    }

    public StubExternalOrdersClient ExternalOrders { get; } = new();

    public StubExternalCustomersClient ExternalCustomers { get; } = new();

    public StubPostProviderClient PostProvider { get; } = new();

    public RecordedMessageBus KafkaMessages { get; } = new();

    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication) =>
        authentication.MapAzureAd("Bearer");

    protected override void ConfigureDatabaseServices(IServiceCollection services) =>
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));

    protected override void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        XBullet.EasyTesting.Hosting.TestScenarioContext context) =>
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseInMemoryDatabase($"{_databaseName}-{context.ScenarioId}"));

    protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
    {
        services.RemoveAll<IExternalOrdersClient>();
        services.AddSingleton(ExternalOrders);
        services.AddScoped<IExternalOrdersClient>(provider =>
            provider.GetRequiredService<StubExternalOrdersClient>());
        services.RemoveAll<IExternalCustomersClient>();
        services.AddSingleton(ExternalCustomers);
        services.AddScoped<IExternalCustomersClient>(provider =>
            provider.GetRequiredService<StubExternalCustomersClient>());
        services.RemoveAll<IPostProviderClient>();
        services.AddSingleton(PostProvider);
        services.AddScoped<IPostProviderClient>(provider =>
            provider.GetRequiredService<StubPostProviderClient>());
        services.RemoveAll<IKafkaPublisher>();
        services.AddSingleton<IKafkaPublisher>(new RecordingKafkaPublisher(KafkaMessages));
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(new TimeProviderStub(
            new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<TimeProviderStub>());
    }

    private sealed class TimeProviderStub(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
```
<!-- end-snippet -->

`ConfigureDatabaseServices` configures the factory database, while
`ConfigureScenarioDatabaseServices` gives each isolated scope a database identified by
`context.ScenarioId`. Always pass the current scope to database helpers so setup and assertions use
the same host as the HTTP request.

`RegisterScenarioResource` makes mutable stubs and the recorder participate in reset and failure
diagnostics. The factory owns these instances. Tests borrow them inside `RunAsync`, which uses the
factory's scenario gate and disposes the scope after the callback finishes. See
[resources and cleanup](../concepts/resources-and-cleanup.md) for failure and cancellation behavior.

## Import an order and verify the complete result

Arrange two line items, enable their feature for this scope, then check the response, persisted
identity and timestamp, external call count, and emitted event:

<!-- snippet: tests/TestStartupApi.IntegrationTests/DocumentationExamples.cs#docs-order-workflow-success -->
```csharp
[Fact]
public Task Import_returns_the_persisted_order_and_publishes_its_event() =>
    RunAsync(async (scope, cancellationToken) =>
    {
        var orders = new OrderScenario(Factory, scope)
            .WithExternalOrder(
                "ext-42", "customer-7", "Replacement parts", 125.50m,
                new ExternalOrderItem("bearing-01", 2, 50m),
                new ExternalOrderItem("seal-02", 1, 25.50m));

        using var result = await orders.Arrange()
            .AsApiUser()
            .PostJson(OrderScenario.ImportUri("ext-42"), new { })
            .ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
        var response = await result.Response.Content.ReadFromJsonAsync<
            OrdersController.OrderResponse>(cancellationToken);
        Assert.NotNull(response);

        var stored = await orders.GetStoredOrderAsync("ext-42", cancellationToken);
        Assert.Equal(stored.Id, response.Id);
        Assert.Equal("ext-42", response.ExternalId);
        Assert.Equal("customer-7", response.CustomerId);
        Assert.Equal(125.50m, response.Total);
        Assert.Equal(ApiUserScenarioExtensions.DefaultObjectId, stored.ImportedBy);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero),
            response.ImportedAt);
        Assert.Equal(stored.ImportedAt, response.ImportedAt);
        Assert.Collection(response.Items,
            item => Assert.Equal(
                new OrdersController.OrderItemResponse("bearing-01", 2, 50m, 100m), item),
            item => Assert.Equal(
                new OrdersController.OrderItemResponse("seal-02", 1, 25.50m, 25.50m), item));
        Assert.Equal(response.Items.Select(item => item.Sku),
            stored.Items.OrderBy(item => item.Sku, StringComparer.Ordinal)
                .Select(item => item.Sku));

        orders.VerifyExternalOrderRequested("ext-42");
        orders.VerifyImportedEvent(stored);
    }, scenario => scenario.EnableFeature(FeatureNames.OrderItems));
```
<!-- end-snippet -->

The [domain scenario](../../tests/TestStartupApi.IntegrationTests/Scenarios/OrderScenario.cs)
keeps repeated setup in one place. `WithExternalOrder` records a desired arrangement; `Arrange()`
attaches it to the request and applies it when `ExecuteAsync` runs. The scenario is single-use.
`GetStoredOrderAsync` reads through the current scope with `AsNoTracking`.

`VerifyImportedEvent` checks that exactly one `orders.imported` Kafka message has the stored order's
payload, the customer's partition key, and the `order-imported` event-type header. A successful HTTP
status alone would miss an incorrect event or an order attributed to the wrong caller.

`AsApiUser()` supplies an object ID and the required scope; it does not create or validate a JWT.
The [identity helper](../../tests/TestStartupApi.IntegrationTests/Scenarios/ApiUserScenarioExtensions.cs)
and [feature helper](../../tests/TestStartupApi.IntegrationTests/Scenarios/FeatureScenarioExtensions.cs)
show the application-specific defaults. `OrderItems` is disabled unless explicitly enabled, so the
scope configuration is part of this example's setup.

## Repeat a request without duplicating its side effects

A second import of the same external order returns `409 Conflict`. Make both requests in one
scope to preserve the first request's state. Call `ArrangeAsync` once, then reuse a client:

<!-- snippet: tests/TestStartupApi.IntegrationTests/DocumentationExamples.cs#docs-order-workflow-duplicate -->
```csharp
[Fact]
public Task Repeating_an_import_does_not_publish_a_second_event() =>
    RunAsync(async (scope, cancellationToken) =>
    {
        var orders = new OrderScenario(Factory, scope)
            .WithExternalOrder("ext-42", "customer-7", "Replacement parts", 125.50m);
        await orders.ArrangeAsync(cancellationToken);
        using var client = scope.Client().AsAzureAdUser(user => user
            .WithObjectId(ApiUserScenarioExtensions.DefaultObjectId)
            .WithScope(AuthorizationScopes.OrdersWrite))
            .WithoutRedirects()
            .Build();

        using var first = await client.PostAsJsonAsync(
            OrderScenario.ImportUri("ext-42"), new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var repeated = await client.PostAsJsonAsync(
            OrderScenario.ImportUri("ext-42"), new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(1, await orders.GetStoredOrderCountAsync(cancellationToken));
        orders.VerifyExternalOrderRequested("ext-42");
        orders.VerifyImportedEvent(
            await orders.GetStoredOrderAsync("ext-42", cancellationToken));
    });
```
<!-- end-snippet -->

An anonymous caller receives `401`; an authenticated caller with only `profile.read` receives `403`.
No upstream response is arranged because neither request should reach the external client. The
zero-call, zero-row, and zero-event assertions make that boundary explicit.

## Reject callers before they reach dependencies

Check both authentication and authorization failures, then assert the absence of side effects:

<!-- snippet: tests/TestStartupApi.IntegrationTests/DocumentationExamples.cs#docs-order-workflow-authorization -->
```csharp
[Theory]
[InlineData(false, HttpStatusCode.Unauthorized)]
[InlineData(true, HttpStatusCode.Forbidden)]
public Task Rejected_callers_cannot_import_or_publish(
    bool authenticated,
    HttpStatusCode expectedStatus) =>
    RunAsync(async (scope, cancellationToken) =>
    {
        var orders = new OrderScenario(Factory, scope);
        var scenario = orders.Arrange();
        if (authenticated)
        {
            scenario.AsAzureAdUser(user => user
                .WithObjectId("user-42")
                .WithScope("profile.read"));
        }
        else
        {
            scenario.AsAnonymous();
        }

        using var result = await scenario
            .PostJson(OrderScenario.ImportUri("ext-42"), new { })
            .ExecuteAsync(cancellationToken);

        Assert.Equal(expectedStatus, result.Response.StatusCode);
        Assert.Equal(0, await orders.GetStoredOrderCountAsync(cancellationToken));
        orders.VerifyExternalOrderRequested("ext-42", expectedCount: 0);
        orders.VerifyNoPublishedEvents();
    });
```
<!-- end-snippet -->

An anonymous caller receives `401`; an authenticated caller with only `profile.read` receives `403`.
No upstream response is arranged because neither request should reach the external client. The
zero-call, zero-row, and zero-event assertions make that boundary explicit.

## Handle a missing upstream order

Arrange a missing order explicitly. A missing arrangement is a test setup error, not a simulated
upstream `404`:

<!-- snippet: tests/TestStartupApi.IntegrationTests/DocumentationExamples.cs#docs-order-workflow-missing -->
```csharp
[Fact]
public Task Missing_upstream_order_leaves_no_persisted_order_or_event() =>
    RunAsync(async (scope, cancellationToken) =>
    {
        var orders = new OrderScenario(Factory, scope)
            .WithMissingExternalOrder("missing-42");

        using var result = await orders.Arrange()
            .AsApiUser()
            .PostJson(OrderScenario.ImportUri("missing-42"), new { })
            .ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(0, await orders.GetStoredOrderCountAsync(cancellationToken));
        orders.VerifyExternalOrderRequested("missing-42");
        orders.VerifyNoPublishedEvents();
    });
```
<!-- end-snippet -->

This case expects one upstream lookup and no stored order or published event. The application also
maps an upstream `HttpRequestException` to `502`; the strict service stub used here does not arrange
exceptions. For transport failures, delays, cancellation, and HTTP serialization tests, use the
[outbound HTTP guide](outbound-http.md) to replace the HTTP handler while retaining the typed client.

## Diagnose failures and run in CI

| Symptom | Inspect | Likely action |
| --- | --- | --- |
| Expected `201`, got `401` | Client identity and `Bearer` mapping | Select an authenticated identity for the intended scheme |
| Expected `201`, got `403` | `scp` claims | Supply `orders.write`; keep insufficient-scope tests separate |
| No response was arranged | Operation and resource ID in the strict stub error | Arrange the exact lookup inside the scenario; do not add permissive defaults |
| Imported items are empty | Scope's `Features:OrderItems` value | Enable the feature before the host is created |
| An event assertion fails | Recorder's expected/actual diagnostic and stub call history | Compare route, partition key, event type, and payload before changing expectations |
| Database assertion sees another order | Scope passed to database helpers | Use the same isolated scope for seed, request, and query |

`ScopedTest.RunAsync` captures registered resource diagnostics before cleanup when the callback
fails or is cancelled. Keep arrangements and assertions inside the callback so those diagnostics
describe the failure. Dispose request results, clients, and responses with `using`, as the examples
do. Pass the test token to I/O; the runner handles failure cleanup independently of that token.

The shared factory's mutable resources are protected by its scenario gate. Do not bypass the runner
or share those instances across unrelated factories. Give concurrently running workers their own
databases and recorders; see [parallel execution](../concepts/multi-target-and-parallel-execution.md).

For CI, use the same three framework-specific commands above and fail the job on a nonzero exit
code. The repository's normal solution test job includes these examples without an explicit filter.
Keep real infrastructure tests in a separate job with the required runtime and readiness checks.
Do not automatically retry a failed stateful test against its previous database.

## Extend coverage before relying on production infrastructure

| Concern | Coverage in this walkthrough | Additional coverage to add in your application |
| --- | --- | --- |
| Credential validation | Policy behavior with simulated claims | [End-to-end authentication](../concepts/authentication.md#configure-end-to-end-handlers) for issuer, audience, signature, and expiration validation |
| HTTP adapter | Calls to a service interface | [Outbound HTTP tests](outbound-http.md) for URL, headers, JSON, errors, and cancellation |
| Relational persistence | Stored values using EF Core InMemory | [Provider tests](entity-framework-core.md) with SQLite or the production provider for constraints, transactions, migrations, and concurrency |
| Broker delivery | Logical publication through a recorder | [Container tests](testcontainers.md) for serialization, broker acknowledgements, and consumer behavior |
| Atomic state and event delivery | Successful save followed by recorded publication | Outbox and dispatcher tests if your application requires atomic delivery; the sample has no outbox |
| Repeated requests | Sequential `409` and no second event | Database uniqueness, concurrent requests, and application-specific idempotency-key semantics |
| Shipping | Separate shipment workflow | [Shipment scenarios](../../tests/TestStartupApi.IntegrationTests/OrderShipmentScenarioTests.cs) for allocations, provider calls, and quantity conflicts |

In particular, the sample saves before publishing. If publication fails, an order may remain stored
without an event, and another import will encounter the existing order. The successful test does
not cover recovery from that gap. Likewise, the sample's shipping provider call and database save
are separate operations. Design recovery and duplicate protection in your application use cases,
then exercise those failure paths explicitly.

In a Clean Architecture application, keep these test replacements at infrastructure boundaries and
run the real application use cases and domain rules behind the endpoint. Test helpers belong in
the test project; production projects should not reference EasyTesting packages.

Use synthetic identifiers and payloads. Keep tokens, connection strings, customer details, and
authorization headers out of committed diagnostics and uploaded CI artifacts; follow
[sensitive-data guidance](../concepts/security-and-sensitive-data.md).

## Related guides and APIs

- [End-to-end framework flows](end-to-end-flows.md) for CRUD, HTTP, events, credentials, snapshots,
  Functions, and Aspire.
- [Application boundary guide](application-boundaries.md) for choosing the boundary to replace.
- [Service and configuration overrides](service-and-configuration-overrides.md) for factory and scope setup.
- [Core API reference](../api/packages/xbullet-easytesting.md) for scopes, scenarios, identities, and resources.
- [EF Core API reference](../api/packages/xbullet-easytesting-entityframeworkcore.md)
  for database factories and actions.
- [Messaging API reference](../api/packages/xbullet-easytesting-messaging.md) for recorders and assertions.
- [Sample application](../../tests/TestStartupApi/README.md) for application wiring and shipment behavior.
