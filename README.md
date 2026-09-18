# XBullet.EasyTesting

[![CI](https://github.com/olgerd007/XBullet.EasyTesting/actions/workflows/ci.yml/badge.svg)](https://github.com/olgerd007/XBullet.EasyTesting/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/XBullet.EasyTesting.svg)](https://www.nuget.org/packages/XBullet.EasyTesting)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Reusable infrastructure for integration-testing authenticated ASP.NET Core controllers through an in-memory `TestServer`.

All packages target both .NET 8 and .NET 10.

## Installation

Install only the packages required by a test project. For example:

```shell
dotnet add package XBullet.EasyTesting
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
dotnet add package XBullet.EasyTesting.Aspire
dotnet add package XBullet.EasyTesting.Azure
dotnet add package XBullet.EasyTesting.Observability
dotnet add package XBullet.EasyTesting.Testcontainers
dotnet add package XBullet.EasyTesting.Snapshots
```

Optional packages provide distributed Aspire testing, real containerized dependencies, Azure SDK
test doubles, observability capture, outbound HTTP stubs, message recording, Verify.Xunit
integration, and isolated Azure Functions helpers.

## Packages

- `XBullet.EasyTesting` provides the authenticated ASP.NET Core test host and test-user clients.
- `XBullet.EasyTesting.EntityFrameworkCore` provides generic scoped database actions and an EF-backed test factory.
- `XBullet.EasyTesting.Aspire` runs closed-box distributed tests with resource readiness and failure diagnostics.
- `XBullet.EasyTesting.Azure` provides deterministic Azure SDK responses, credentials, paging, and pipeline transport.
- `XBullet.EasyTesting.Http` provides fluent outbound HTTP stubs and request recording.
- `XBullet.EasyTesting.Messaging` provides transport-neutral published-message recording.
- `XBullet.EasyTesting.Observability` captures structured logs, distributed traces, and metrics, with deterministic time support.
- `XBullet.EasyTesting.Testcontainers` provides scenario-scoped real PostgreSQL, SQL Server, Kafka, Redis, RabbitMQ, Azurite, and Service Bus emulator dependencies.
- `XBullet.EasyTesting.AzureFunctions` provides isolated-worker contexts and fluent HTTP, timer, and Kafka trigger data.
- `XBullet.EasyTesting.Snapshots` provides framework-independent JSON snapshot assertions.
- `XBullet.EasyTesting.Verify.Xunit` provides the optional Verify.Xunit v3 adapter and depends on the snapshots package.

The repository keeps framework behavior tests in `tests/XBullet.EasyTesting.Tests`, sample API scenarios in `tests/TestApi.IntegrationTests`, and function scenarios in `tests/TestFunctions.IntegrationTests`.

## Controller authentication

Reference `XBullet.EasyTesting`, make the API entry point visible to the test project, and create a factory:

```csharp
public partial class Program;

public sealed class OrdersControllerTests
    : IClassFixture<AuthenticatedWebApplicationFactory<Program>>
{
    private readonly AuthenticatedWebApplicationFactory<Program> _factory;

    public OrdersControllerTests(AuthenticatedWebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Admin_can_get_orders()
    {
        using var client = _factory.Client()
            .AsUser(user => user
                .WithName("Ada")
                .WithNameIdentifier("user-42")
                .WithRole("Administrator")
                .WithClaim("permission", "orders.read"))
            .WithoutRedirects()
            .Build();
        var response = await client.GetAsync("/api/orders");

        response.EnsureSuccessStatusCode();
    }
}
```

The fluent client is anonymous until `AsUser` is called. Use `factory.Client().AsAnonymous().Build()` or `CreateAnonymousClient()` for `401 Unauthorized` scenarios. A user with insufficient roles or claims receives `403 Forbidden`. `AuthenticateAs` can also be applied to an individual `HttpRequestMessage`, so identities can vary while sharing one client and test server. Existing non-fluent APIs remain available.

For composable configuration without a custom factory subclass, build the host fluently. Multiple callbacks of each kind are applied in registration order, allowing extension packages and test-specific setup to participate independently:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .ConfigureConfiguration(configuration =>
        configuration.AddInMemoryCollection(testSettings))
    .ConfigureServices(services =>
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    })
    .ConfigureAuthentication(authentication => authentication
        .MapAzureAd("Bearer")
        .MapApiKey("ApiKey"))
    .Build();
```

Compose one arrange-and-request flow with `Scenario()`. The result owns both its client and response, so dispose it after assertions:

```csharp
using var result = await factory.Scenario()
    .Arrange(cancellationToken => SeedOrdersAsync(cancellationToken))
    .AsAzureAdUser(user => user
        .WithTenantId("tenant-42")
        .WithScope("orders.read"))
    .WithHeader("X-Correlation-Id", "test-42")
    .Get("/api/orders")
    .ExecuteAsync(cancellationToken);

await result.Should()
    .HaveStatusCode(HttpStatusCode.OK)
    .HaveJsonBodyAsync(
        new OrderResponse(42, "Ready"),
        cancellationToken: cancellationToken);
```

Response assertions are test-framework agnostic. In addition to status and structural JSON body
checks, a scenario result can assert successful responses and response or content headers.

## Per-test isolation

Use `TestScenarioScope` when a factory is shared by multiple tests. The factory holds its scenario gate for the complete test lifetime, creates a child host for reversible configuration and service replacements, resets registered mutable resources before and after the test, and invokes provider-specific database isolation hooks.

Register reusable HTTP stubs and message recorders once in the factory. Both implement `ITestScenarioResource`, so their rules, requests, and messages are reset automatically and captured if the test fails:

```csharp
public sealed class TestApiFactory
    : EntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
    public StubHttpMessageHandler ExternalCatalog { get; } = new();
    public RecordedMessageBus PublishedMessages { get; } = new();

    public TestApiFactory()
    {
        RegisterScenarioResource("External catalog", ExternalCatalog);
        RegisterScenarioResource("Published messages", PublishedMessages);
    }

    protected override void ConfigureScenarioDatabaseServices(
        IServiceCollection services,
        TestScenarioContext context)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context.OnCleanup(connection.DisposeAsync);
        services.AddDbContext<TestApiDbContext>(options => options.UseSqlite(connection));
    }
}
```

If a test does not depend on relational behavior, the EF Core in-memory provider can be selected
without any database registration boilerplate:

```csharp
public sealed class TestApiFactory
    : InMemoryEntityFrameworkWebApplicationFactory<Program, TestApiDbContext>
{
}
```

Each scenario gets its own in-memory database. Because this provider does not enforce relational
constraints or support transactions, prefer SQLite or the production relational provider for tests
that exercise those behaviors.

Run the test body through `RunInTestScenarioScopeAsync` to guarantee failure diagnostics are captured before cleanup. The original exception is preserved, and `TestScenarioDiagnostics` is attached through `exception.Data[TestScenarioDiagnostics.ExceptionDataKey]`:

```csharp
await factory.RunInTestScenarioScopeAsync(
    async (scope, cancellationToken) =>
    {
        factory.ExternalCatalog
            .When(HttpMethod.Get, "/products/701")
            .RespondJson(externalProduct);

        using var client = scope.Client()
            .AsUser(TestUser.Create("scenario-user"))
            .Build();
        using var response = await client.PostAsync(
            "/api/products/import/701",
            content: null,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    },
    configure: scope => scope
        .ConfigureConfiguration(configuration =>
            configuration.AddInMemoryCollection(testSettings))
        .ConfigureServices(services =>
            services.AddSingleton<IClock>(fakeClock)),
    cancellationToken);
```

For manual lifetime control, use `await using var scope = await factory.CreateTestScenarioScopeAsync(...)`. Override `ConfigureScenarioDatabaseServices` to create a database or schema named from `context.ScenarioId`; scenario-owned connections and containers can be registered with `DisposeWithScenario` or `OnCleanup`.

External dependencies that must start before the application reads its configuration can implement
`ITestScenarioEnvironmentResource`. Configure a fresh resource for each scenario; after startup it
can contribute dynamic configuration and service registrations, and the scope makes the typed
resource available to test code:

```csharp
using var factory = EasyTestHost.Create<Program>()
    .ConfigureEnvironment(environment => environment
        .AddResource("database", context =>
            new CustomDatabaseResource(context.ScenarioId)))
    .Build();

await using var scope = await factory.CreateTestScenarioScopeAsync(cancellationToken: cancellationToken);
var database = scope.GetEnvironmentResource<CustomDatabaseResource>("database");
```

The lifecycle is startup and readiness, configuration and service registration, host startup,
failure diagnostics, host shutdown, and reverse-order resource disposal. Derived factories can
override `ConfigureScenarioEnvironment`. The upcoming optional container packages build on this
lifecycle without adding container dependencies to the core package.

For application-specific dependency replacement, derive from the factory and override `ConfigureServicesForTests`:

```csharp
public sealed class ApiFactory : AuthenticatedWebApplicationFactory<Program>
{
    protected override void ConfigureServicesForTests(IServiceCollection services)
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FakeClock());
    }
}
```

### Startup-based hosts

Applications that already expose an `IntegrationTestStartup` can keep that host and avoid invoking
`Program.Main`. Derive from `StartupAuthenticatedWebApplicationFactory<TStartup>`; it builds the
startup pipeline directly on `TestServer`, uses the startup assembly output as its content root, and
retains all configuration, authentication, and scenario hooks:

```csharp
public sealed class StartupTestHost
    : StartupAuthenticatedWebApplicationFactory<IntegrationTestStartup>
{
    protected override void ConfigureTestConfiguration(IConfigurationBuilder configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = "https://identity.example.test"
        });

    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication) =>
        authentication.MapFederation("Federation");
}
```

Use `StartupEntityFrameworkWebApplicationFactory<TStartup, TDbContext>` when the same host also
needs the framework database helpers and per-scenario database isolation.

The Federation profile creates a primary identity whose authentication type defaults to
`Federation` and an additional identity whose authentication type defaults to `ApiUserIdentity`:

```csharp
using var client = scope.Client()
    .AsFederatedUser(user => user
        .WithNameIdentifier("user-42")
        .WithFederationClaim("tenant", "tenant-42")
        .WithApiUserClaim("portfolio", "portfolio-17"))
    .Build();
```

If an application requires the additional identity to be a concrete `ApiUserIdentity` subclass, derive from
`TestClaimsPrincipalFactory`, override `CreateAdditionalIdentity`, and replace
`ITestClaimsPrincipalFactory` in `ConfigureServicesForTests` (or
`ConfigureAdditionalServicesForTests` on the EF host). The transported user profile remains
serializable while the application controls the server-side identity type.

The test authentication handler is installed only in the test host. It is never registered by the application itself.

### Multiple authentication schemes

Map test profiles to the scheme names used by the application. Mapped schemes replace their production handlers only inside the test server, so policies that explicitly select `Bearer`, `ApiKey`, or another scheme remain testable:

```csharp
public sealed class ApiFactory : AuthenticatedWebApplicationFactory<Program>
{
    protected override void ConfigureTestAuthentication(
        TestAuthenticationSchemeBuilder authentication)
    {
        authentication
            .MapAzureAd("Bearer")
            .MapApiKey("ApiKey")
            .MapFederation("Federation")
            .MapScheme("PartnerScheme");
    }
}
```

Create scheme-specific clients fluently:

```csharp
using var azureAdClient = factory.Client()
    .AsAzureAdUser(user => user
        .WithObjectId("user-42")
        .WithTenantId("tenant-42")
        .WithPreferredUsername("ada@example.test")
        .WithScopes("orders.read", "profile")
        .WithAppRole("Administrator"))
    .Build();

using var apiKeyClient = factory.Client()
    .AsApiKey(apiKey => apiKey
        .WithKeyId("partner-key")
        .WithClientName("Fulfilment partner")
        .WithClaim("region", "eu"))
    .Build();
```

Azure AD profiles expose common `oid`, `tid`, `preferred_username`, `scp`, `roles`, and `azp` claims. API-key profiles expose a non-secret `api_key_id`. Both support custom claims and roles. For another authentication type, map its scheme and use `TestUser.CreateBuilder().WithAuthenticationScheme(...)`.

These profiles test controller authentication and authorization without external identity infrastructure. They intentionally bypass signature, issuer, token-expiry, and secret validation.

### End-to-end authentication

For validation through the application's real handlers, keep `AddJwtBearer` and the application's API-key handler registered in production code, then opt the test factory into end-to-end mode:

```csharp
protected override void ConfigureTestAuthentication(
    TestAuthenticationSchemeBuilder authentication)
{
    authentication
        .UseEndToEndJwt("Bearer", authority => authority
            .WithIssuer("https://identity.orders.test")
            .WithAudience("orders-api")
            .WithTokenLifetime(TimeSpan.FromMinutes(2))
            .WithClockSkew(TimeSpan.Zero)
            .SaveAccessToken())
        .UseEndToEndApiKey("ApiKey", apiKey => apiKey
            .WithHeaderName("X-Api-Key")
            .WithQueryParameterName("api_key"))
        .UseEndToEndClientCertificate("Certificate");
}
```

`UseEndToEndJwt` connects the named `JwtBearerHandler` to an in-memory OIDC backchannel. The real handler loads discovery metadata and signing keys, validates the token, and refreshes JWKS after key rotation. The same OIDC discovery and JWKS documents are exposed from the test host at `/.well-known/openid-configuration` and `/.well-known/jwks.json`. Tokens can customize issuer, audience, expiry, scopes, roles, subject, name, and arbitrary claims:

```csharp
using var client = scope.Client()
    .AsJwt(token => token
        .WithSubject("user-42")
        .WithName("Ada")
        .WithAudience("orders-api")
        .WithScope("orders.read")
        .WithRole("Administrator")
        .ExpiresAfter(TimeSpan.FromMinutes(1)))
    .Build();
```

Negative scenarios are explicit: `AsExpiredJwt()`, `AsMalformedJwt()`, `AsJwtWithWrongAudience()`, `AsJwtWithWrongIssuer()`, `AsJwtWithInvalidSignature()`, `AsJwtWithUnknownKey()`, `AsUnsignedJwt()`, and `AsJwtNotYetValid()`. Real API keys can be injected with `WithApiKeyHeader(value)` or `WithApiKeyQuery(value)`; these helpers only transport the credential, leaving parsing and validation to the application's registered API-key handler.

Register multiple authorities with unique discovery and JWKS paths, then select one by scheme. Authorities are also available from the scenario scope for explicit rotation:

```csharp
authentication.UseEndToEndJwt("PartnerBearer", authority => authority
    .WithIssuer("https://partner.orders.test")
    .WithAudience("orders-partner-api")
    .WithDiscoveryPath("/.well-known/partner/openid-configuration")
    .WithJwksPath("/.well-known/partner/jwks.json")
    .WithoutDefaultScheme());

using var partnerClient = scope.Client()
    .AsJwt("PartnerBearer", token => token.WithClaim("partner", "trusted"))
    .Build();

scope.JwtAuthority("PartnerBearer").RotateSigningKey();
```

Authentication events are reset with each `TestScenarioScope`, included in failure diagnostics, and contain no headers, query strings, or raw credentials. Assertions can be chained after a request:

```csharp
scope.AuthenticationEvents.Should()
    .HaveValidationFailure(authenticationScheme: "Bearer")
    .HaveChallenge(authenticationScheme: "Bearer")
    .NotHave(TestAuthenticationEventKind.Forbidden);
```

For mTLS, leave the application's real `AddCertificate` scheme registered and use `WithClientCertificate(...)`. XBullet creates a self-signed client certificate, transports it through TestServer as an HTTPS connection certificate, removes the internal transport header, and lets `CertificateAuthenticationHandler` perform validation:

```csharp
using var client = scope.Client()
    .WithClientCertificate(certificate => certificate
        .WithSubject("CN=trusted-test-client"))
    .Build();
```

Calling `SaveAccessToken()` on the authority sets the real bearer handler's `SaveToken` option, making the access token available through `AuthenticateAsync().Properties`. A standalone authority can be created with `using var authority = TestJwtAuthority.Create(...)` when a token is needed outside the HTTP client builder.

Simulated users can represent composite principals and authentication-ticket state when handler validation is not under test:

```csharp
using var client = scope.Client()
    .AsUser(user => user
        .WithName("Primary identity")
        .WithIdentity(identity => identity
            .WithAuthenticationType("DelegatedIdentity")
            .WithClaim(ClaimTypes.Name, "Secondary identity"))
        .WithAuthenticationProperty("refresh_token", "test-value"))
    .Build();
```

Anonymous controllers continue to work normally. When the application uses a fallback policy that requires authentication, mark public controllers such as health endpoints with `[AllowAnonymous]` and test them with `CreateAnonymousClient()`.

The sample API also demonstrates a database-backed controller. `TestApiFactory` derives from `EntityFrameworkWebApplicationFactory<Program, TestApiDbContext>` and replaces its file-based EF Core SQLite registration with an open in-memory SQLite connection. This exercises controller routing, authorization, EF Core queries, mapping, and serialization without sharing persistent data between test fixtures.

The EF Core factory provides generic operations:

```csharp
await factory.InitializeDatabaseAsync();
await factory.RecreateDatabaseAsync();
await factory.SeedDatabaseAsync([new Product { Id = 42, Name = "Keyboard" }]);

var count = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products.CountAsync(cancellationToken));

await factory.ExecuteDatabaseAsync((database, cancellationToken) =>
{
    database.Products.Add(new Product { Id = 43, Name = "Mouse" });
    return Task.CompletedTask;
});

await factory.ExecuteInTransactionAsync(async (database, cancellationToken) =>
{
    // Changes are saved and committed when the callback succeeds.
});
```

Related setup can also be composed and saved once as a fluent database scenario:

```csharp
await factory.Database()
    .Recreate()
    .Seed(new Product { Id = 42, Name = "Keyboard" })
    .Apply(database =>
    {
        database.Products.Local.Single(product => product.Id == 42).Name = "Keyboard Pro";
    })
    .InTransaction()
    .ExecuteAsync(cancellationToken);
```

Each action resolves a fresh scoped `DbContext`. Operations invoked through the factory are serialized to avoid overlapping reset/seed operations. `RecreateDatabaseAsync` deletes the entire configured database, so the factory must point to an isolated test database.

### CRUD controller example

The sample `ProductsController` is an authenticated, database-backed CRUD resource:

| Method | Route | Result |
| --- | --- | --- |
| `GET` | `/api/products` | Ordered product collection |
| `GET` | `/api/products/{id}` | Product or `404 Not Found` |
| `POST` | `/api/products` | Persisted product and `201 Created` with `Location` |
| `PUT` | `/api/products/{id}` | Updated product or `404 Not Found` |
| `DELETE` | `/api/products/{id}` | `204 No Content` or `404 Not Found` |

Create and update requests validate the required 200-character name and positive price through `[ApiController]`. The integration tests recreate or seed the database, send real JSON requests through TestServer, and query a fresh EF Core context to confirm the final stored state.

## External APIs and database persistence

Reference `XBullet.EasyTesting.Http` and replace the primary handler of the application's named or typed `HttpClient` in the test factory:

```csharp
public StubHttpMessageHandler ExternalCatalog { get; } = new();

protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
{
    services
        .AddHttpClient<IExternalCatalogClient, ExternalCatalogClient>()
        .ConfigurePrimaryHttpMessageHandler(() => ExternalCatalog)
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
}
```

Arrange an exact outbound response, call the controller, and inspect the database through the EF factory:

```csharp
factory.ExternalCatalog
    .Reset()
    .When(HttpMethod.Get, "/products/701")
    .WithRequestHeader("X-Tenant", "tenant-42")
    .RespondJson(new { Id = 701, Name = "Keyboard", Price = 149.95m });

using var client = factory.Client()
    .AsUser(user => user.WithName("Importer"))
    .Build();

using var response = await client.PostAsync("/api/products/import/701", null);

var savedProduct = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products
        .SingleAsync(product => product.Id == 701, cancellationToken));

Assert.Equal(HttpStatusCode.Created, response.StatusCode);
factory.ExternalCatalog.VerifyCalled(HttpMethod.Get, "/products/701");
```

The test API also has a bulk synchronization workflow at `POST /api/products/sync`.
It requests a category and limit from the external catalog, updates existing products,
inserts new products, and stores a `CatalogSyncRun` audit record. Upstream failures
return `502 Bad Gateway` and are retained as failed sync runs without changing products.

```csharp
factory.ExternalCatalog
    .When(HttpMethod.Get, "/products")
    .WithQueryParameter("category", "computer accessories")
    .WithQueryParameter("limit", "10")
    .RespondJson(externalProducts);

using var response = await client.PostAsJsonAsync(
    "/api/products/sync",
    new { Category = "computer accessories", Limit = 10 });

var syncRun = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.CatalogSyncRuns
        .SingleAsync(cancellationToken));

Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal(CatalogSyncStatus.Completed, syncRun.Status);
```

Rules can match query parameters, headers, exact text bodies, structural JSON bodies, or a custom `StubHttpRequest` predicate. Query matching is independent of parameter order and supports decoded and repeated values:

```csharp
stub
    .When(HttpMethod.Get, "/products")
    .WithQueryParameter("category", "books")
    .WithQueryParameter("tag", values => values.Contains("featured"))
    .RespondJson(products);
```

Match a complete JSON request body, a root property, or a nested path. JSON-path matching supports dot-separated properties and zero-based array indexes:

```csharp
stub
    .When(HttpMethod.Post, "/orders")
    .WithJsonProperty("tenantId", "tenant-42")
    .WithJsonPath("$.customer.id", 701)
    .WithJsonPath("$.items[0].quantity", value => value.GetInt32() > 0)
    .Respond(HttpStatusCode.Created);
```

`Respond`, `RespondJson`, and `RespondText` cover empty, JSON, and text responses; callback and asynchronous responses can inspect the captured request. `Throw` simulates network failures. `VerifyCalled`, `VerifyNotCalled`, and `Verify` assert interactions and report every recorded request when verification fails. Every call remains available through `Requests`, including its method, URI, headers, and body.

Return a different response for each consecutive matching request with an explicit sequence. Each entry is consumed once, and an additional call throws `StubHttpSequenceExhaustedException`:

```csharp
stub
    .When(HttpMethod.Get, "/catalog/status")
    .RespondSequence(sequence => sequence
        .Respond(HttpStatusCode.ServiceUnavailable)
        .WithDelay(TimeSpan.FromMilliseconds(100))
        .RespondJson(new { ready = true }));
```

Use `WithDelay(...)` before any ordinary response to delay every matching call. `TimeoutAfter(...)` throws a deterministic `TimeoutException` after the specified duration, while `Timeout()` waits for the caller's cancellation token or `HttpClient.Timeout`.

Explicit fault helpers cover cancellation and malformed payload handling:

```csharp
stub.When(HttpMethod.Get, "/cancelled").Cancel();
stub.When(HttpMethod.Get, "/cancel-later")
    .CancelAfter(TimeSpan.FromMilliseconds(100));
stub.When(HttpMethod.Get, "/bad-json")
    .RespondMalformedJson("{\"incomplete\":");
stub.When(HttpMethod.Get, "/truncated")
    .RespondTruncated("{\"partial\":", mediaType: "application/json");
```

`RespondMalformedJson` rejects valid JSON during rule configuration. `RespondTruncated` returns headers successfully and throws an I/O failure when the application consumes the partial body. These helpers are also available as response-sequence entries.

An unmatched request receives `501 Not Implemented` with diagnostics for every configured rule, including method and URI differences, failed query/header/body predicates, and exceptions thrown by custom predicates.

When `XBullet.EasyTesting.Snapshots` is referenced, snapshot one request or every request captured by the handler with the same update, scrubber, acceptance, and diff-viewer workflow used by controller snapshots:

```csharp
await stub.ShouldMatchRequestsSnapshot(
    new StubHttpRequestSnapshotOptions()
        .IgnoringHeaders("X-Request-Nonce"),
    new SnapshotSettings()
        .ScrubMembers("timestamp", "requestId"));
```

JSON request bodies are captured structurally. Authorization, cookies, API keys, correlation IDs, and tracing headers are excluded by default; use `IncludingHeader` to opt one back in. Use `ShouldMatchRequestSnapshot` on an individual `StubHttpRequest`.

## Kafka, Azure Service Bus, and notifications

Reference `XBullet.EasyTesting.Messaging` to test controllers at the application publisher boundary without running a broker or depending on a particular vendor SDK. Keep the application's publisher interface in production code and adapt it to `RecordedMessageBus` in the test project:

```csharp
internal sealed class RecordingPublisher(RecordedMessageBus messages)
    : IApplicationMessagePublisher
{
    public Task PublishAsync<T>(
        string transport,
        string destination,
        T message,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken) =>
        messages.RecordAsync(
            transport,
            destination,
            message,
            headers,
            cancellationToken);
}
```

Replace the real Kafka, Service Bus, or Notification Hubs adapter in the test factory and expose the recorder:

```csharp
public RecordedMessageBus PublishedMessages { get; } = new();

protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
{
    services.RemoveAll<IApplicationMessagePublisher>();
    services.AddSingleton<IApplicationMessagePublisher>(
        new RecordingPublisher(PublishedMessages));
}
```

Then call the controller and assert the destination, payload, and broker-specific headers:

```csharp
factory.PublishedMessages.Reset();

using var response = await client.PostAsJsonAsync(
    "/api/publishing/kafka/orders",
    new { OrderId = 42, CustomerId = "customer-7", Total = 125.50m });

Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
factory.PublishedMessages.Should()
    .ContainSingle(MessageTransportNames.Kafka, "orders.created")
    .HaveHeader("partition-key", "customer-7")
    .HavePayload(new OrderCreatedMessage(42));
```

Well-known names are included for Kafka, Azure Service Bus, and Azure Notification Hubs. `RecordAsync` also accepts any custom transport or destination, so the same pattern covers RabbitMQ, Event Hubs, SNS/SQS, email, webhooks, or application-specific notification providers. Payloads and headers are copied at publication time to prevent later mutation from changing assertions.

## Azure Functions isolated worker

Reference `XBullet.EasyTesting.AzureFunctions` to resolve function classes from a test service provider and invoke isolated-worker entry points directly. `TestFunctionContext` supplies non-null trace, binding, retry, function-definition, feature, item, service, and cancellation state:

```csharp
var recorder = new RecordingTriggerInvocationSink();
await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<ProcessOrderHttpFunction>()
    .AddFunction<CleanupTimerFunction>()
    .AddFunction<ProcessOrderKafkaFunction>()
    .AddFunction<AdditionalTriggerFunctions>()
    .UseMiddleware(async (context, next) =>
    {
        context.Items["test-middleware"] = "before";
        await next(context);
        context.Items["test-middleware"] = "after";
    })
    .ConfigureServices(services =>
        services.AddSingleton<ITriggerInvocationSink>(recorder))
    .Build();
```

Build an in-memory HTTP request and inspect the returned response:

```csharp
var request = host.HttpRequest(nameof(ProcessOrderHttpFunction))
    .WithMethod(HttpMethod.Post)
    .WithUrl("/api/orders")
    .WithJsonBody(new CreateOrderRequest("order-42", 3))
    .Build();

var function = host.GetRequiredService<ProcessOrderHttpFunction>();
var response = await function.RunAsync(request, request.FunctionContext);
var body = await response.ReadBodyAsJsonAsync<AcceptedOrderResponse>();
```

HTTP requests are automatically captured as an `httpTrigger` input. Retry, tracing, custom binding data, items, and typed invocation features can be configured fluently:

```csharp
var context = host.CreateContext("ProcessOrder")
    .WithRetry(retryCount: 2, maxRetryCount: 5)
    .WithTrace(traceParent, traceState)
    .WithBindingData("tenant", "test-tenant")
    .WithFeature(new TestFeature());
```

Timer and Kafka values retain their simple direct builders and also provide capturable trigger forms through `BuildTrigger`, `JsonTrigger`, and `JsonBatchTrigger`:

```csharp
var timer = AzureFunctionTestHost.Timer()
    .PastDue()
    .WithSchedule(last, next)
    .Build();

var kafkaMessage = KafkaTriggerData.Json(
    new KafkaOrderMessage("order-42", 3));
```

Kafka-triggered functions can be composed with outbound HTTP stubs. The sample
`PriceOrderKafkaFunction` consumes an order-pricing event, fetches the product price from an
external API, and records the calculated total:

```csharp
var pricingApi = new StubHttpMessageHandler();
pricingApi
    .When(HttpMethod.Get, "/products/42/price")
    .RespondJson(new { ProductId = 42, UnitPrice = 19.95m, Currency = "USD" });

await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<PriceOrderKafkaFunction>()
    .ConfigureServices(services =>
    {
        services.AddSingleton<ITriggerInvocationSink>(recorder);
        services
            .AddHttpClient<IOrderPricingClient, OrderPricingClient>(client =>
                client.BaseAddress = new Uri("https://pricing.example.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => pricingApi);
    })
    .Build();

var trigger = KafkaTriggerData.JsonTrigger(
    new OrderPricingRequestedMessage("order-42", ProductId: 42, Quantity: 3),
    topic: "order-pricing");

await host.InvokeAsync<PriceOrderKafkaFunction, string>(
    nameof(PriceOrderKafkaFunction),
    trigger,
    (function, message, context) => function.RunAsync(message, context));

pricingApi.VerifyCalled(HttpMethod.Get, "/products/42/price");
```

The function lets HTTP failures propagate so the deployed Kafka trigger can retry the event;
the integration test also verifies that a failed pricing call produces no recorded result.

Service Bus, Queue Storage, Blob, Event Grid, and Event Hubs builders return `TestTriggerData<T>`. Passing it to `InvokeAsync` captures the input and binding metadata, runs configured worker middleware in order, and invokes the function:

```csharp
var trigger = AzureFunctionTestHost.ServiceBusTrigger()
    .WithJsonBody(new OrderMessage("order-42", 3))
    .WithMessageId("message-1")
    .WithCorrelationId("correlation-1")
    .Build();

var invocation = await host.InvokeAsync<OrderFunction, string>(
    "ProcessOrderServiceBus",
    trigger,
    (function, message, context) => function.RunAsync(message, context));

Assert.Equal("message-1", invocation.Context.BindingContext.BindingData["MessageId"]);
Assert.Equal(trigger.Value, invocation.Context.Bindings.GetInput<string>("message"));
```

Equivalent entry points are `QueueTrigger()`, `BlobTrigger()`, `EventGridTrigger()`, and `EventHubsTrigger()`. Builders expose trigger-specific payload and metadata methods, including Event Hubs batches and Blob streams.

For functions returning a multiple-output POCO, the invocation captures every public result property and recognizes worker output attributes such as `QueueOutput` and `BlobOutput`:

```csharp
var invocation = await host.InvokeAsync<RouteOrderFunction, RouteOrderOutput>(
    context,
    (function, testContext) => function.RunAsync(trigger.Value, testContext));

invocation.Context.Bindings.Should()
    .HaveCount(2)
    .HaveValue("QueueMessage", expectedQueueMessage)
    .HaveValue("BlobDocument", expectedBlobDocument)
    .NotContain("UnexpectedOutput");
```

These tests exercise function code, dependency injection, serialization, binding metadata, middleware ordering, retry-aware behavior, and output behavior without Azure Functions Core Tools or live Azure services. They do not validate host indexing, binding expressions, broker connectivity, checkpoints, or deployment configuration; keep a smaller runtime-level test suite for those concerns.

## Built-in snapshots

Reference the separate `XBullet.EasyTesting.Snapshots` package to use snapshot assertions without a dependency on xUnit, Verify, or another test framework:

```csharp
[Fact]
public async Task Get_order_matches_snapshot()
{
    using var client = _factory.Client()
        .AsUser(user => user.WithName("Ada").WithRole("Administrator"))
        .Build();
    using var response = await client.GetAsync("/api/orders/42");

    var options = new ControllerSnapshotOptions()
        .IgnoringHeaders("ETag");
    var settings = new SnapshotSettings()
        .Named("administrator-order")
        .ScrubMembers("Id", "CreatedAt")
        .IgnoreMembers("AccessToken")
        .ScrubGuids()
        .ScrubDateTimes();

    await response.ShouldMatchControllerSnapshot(options, settings);
}
```

The first run writes `__snapshots__/TestFile.TestMethod.received.json` and fails with a `SnapshotMismatchException`. Review the file and rename it to `.verified.json` to approve it. Later mismatches write a new received file while preserving the approved snapshot.

The lower-level assertion works with any serializable value:

```csharp
await SnapshotAssert.MatchAsync(result);
```

Raw JSON content has a dedicated assertion so it is parsed and normalized rather than captured as
an escaped JSON string. Serialization uses `System.Text.Json`, and the generated snapshots retain
the `.received.json` and `.verified.json` extensions:

```csharp
var settings = new SnapshotSettings()
    .ScrubGuids();

await SnapshotAssert.MatchJsonAsync(json, settings);

using var response = await client.GetAsync("/api/orders/42");
response.EnsureSuccessStatusCode();
await response.Content.ShouldMatchJsonSnapshot();
```

Use `MatchJsonAsync` for a raw JSON string and `ShouldMatchJsonSnapshot` when only an HTTP response
body belongs in the snapshot. Use `ShouldMatchControllerSnapshot` when request metadata, status,
and stable headers should be included too.

Structured scrubbers are applied recursively to objects and arrays. `ScrubMembers` preserves a member but stores `{Scrubbed}` instead of its dynamic value; `IgnoreMembers` removes it. Member matching is case-insensitive. `ScrubGuids` and `ScrubDateTimes` replace matching JSON string values with `{Guid}` and `{DateTime}`. For specialized transformations, the existing `Scrub(content => ...)` string scrubber remains available. Parameterized tests should set a unique snapshot name for each case.

### Diff viewer and snapshot acceptance

On a local non-CI run, a mismatch automatically opens a detected Visual Studio, VS Code, Rider, or Meld diff viewer. Disable this behavior or select a tool explicitly:

```csharp
var settings = new SnapshotSettings()
    .WithoutDiffTool();

var vscodeSettings = new SnapshotSettings()
    .WithDiffTool(SnapshotDiffTool.VisualStudioCode());
```

Promote a received file explicitly:

```csharp
SnapshotAssert.AcceptReceived(exception.ReceivedPath);
```

Automatic update modes are opt-in:

```csharp
var settings = new SnapshotSettings()
    .Updating(SnapshotUpdateMode.Missing); // or All
```

They can also be selected for a test run with `INTEGRATION_TESTS_UPDATE_SNAPSHOTS=missing` or `INTEGRATION_TESTS_UPDATE_SNAPSHOTS=all`. `missing` creates only absent verified files; `all` also replaces changed verified files. Never enable `all` in a normal CI verification run.

## Verify.Xunit v3 controller snapshots

Reference the optional `XBullet.EasyTesting.Verify.Xunit` package from an xUnit v3 test project and verify an HTTP response directly:

```csharp
[Fact]
public async Task Get_order_matches_snapshot()
{
    using var client = _factory.CreateAuthenticatedClient(
        TestUser.Create(name: "Ada", roles: ["Administrator"]));
    using var response = await client.GetAsync("/api/orders/42");

    await response.VerifyControllerSnapshot();
}
```

The Verify snapshot uses the same normalized controller model as the built-in assertion: request method and relative URL, numeric status code, reason phrase, stable response headers, and body. JSON is compared structurally. Empty bodies become `null`, text remains text, and binary content is stored as base64.

Volatile headers such as `Date`, `Server`, and correlation identifiers are excluded by default. Customize the captured data when needed:

```csharp
var options = new ControllerSnapshotOptions()
    .WithoutRequest()
    .IgnoringHeaders("ETag");

var settings = new VerifySettings();
settings.ScrubMember("createdAt");

await response.VerifyControllerSnapshot(options, settings);
```

Commit each accepted `*.verified.txt` file. Unaccepted `*.received.*` files are ignored by this repository.

## Building and releasing

Build and test the complete solution locally:

```shell
dotnet restore XBullet.EasyTesting.sln
dotnet build XBullet.EasyTesting.sln --configuration Release --no-restore
dotnet test XBullet.EasyTesting.sln --configuration Release --no-build
```

Tests that start Aspire processes or Docker containers are explicit. Run the real dependency suite
separately when validating infrastructure changes:

```shell
dotnet test tests/XBullet.EasyTesting.ContainerTests/XBullet.EasyTesting.ContainerTests.csproj \
  --configuration Release --explicit only
```

The regular CI matrix compiles these projects but skips their explicit runtime tests to keep feedback
fast; run them locally or from a manually provisioned environment when changing infrastructure support.

GitHub Actions builds and tests on Windows and Ubuntu, records Cobertura code coverage, validates public API approvals, checks package compatibility against the latest stable release, and creates packages for pushes and pull requests.

Publishing uses NuGet.org trusted publishing instead of a long-lived API key. Configure a GitHub trusted publisher for the `olgerd007/XBullet.EasyTesting` repository and `.github/workflows/publish-nuget.yml`, update `CHANGELOG.md`, and publish a GitHub Release with a semantic-version tag such as `v1.2.3`. The release workflow exchanges its GitHub OIDC token for a short-lived NuGet API key, then publishes all `XBullet.EasyTesting.*` packages and their symbol packages.

Public API approval files live beside each package project. New intentional APIs belong in `PublicAPI.Unshipped.txt`; move them to `PublicAPI.Shipped.txt` when preparing a stable release. Unapproved public changes and binary compatibility breaks fail the build or package step.

### Preview flow

Every CI run creates preview packages using the current `VersionPrefix` and the workflow run number, for example `1.0.4-preview.42`. Download the `nuget-preview-42` workflow artifact and use its directory as a local NuGet source to test the complete package set without publishing it.

To publish a public preview to NuGet.org, create a GitHub Release with a tag such as `v1.0.4-preview.1` and select **Set as a pre-release**. The release workflow verifies that the GitHub release type and semantic version agree before publishing. Install public previews with:

```shell
dotnet add package XBullet.EasyTesting --prerelease
```

For a stable release, use a tag without a suffix, such as `v1.0.4`, and do not mark the GitHub Release as a pre-release.

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and [SECURITY.md](SECURITY.md) for private vulnerability reporting.
