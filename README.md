# XBullet.EasyTesting

[![CI](https://github.com/olgerd007/XBullet.EasyTesting/actions/workflows/ci.yml/badge.svg)](https://github.com/olgerd007/XBullet.EasyTesting/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/XBullet.EasyTesting.svg)](https://www.nuget.org/packages/XBullet.EasyTesting)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Reusable infrastructure for integration-testing authenticated ASP.NET Core controllers through an in-memory `TestServer`.

## Installation

Install only the packages required by a test project. For example:

```shell
dotnet add package XBullet.EasyTesting
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
dotnet add package XBullet.EasyTesting.Snapshots
```

Optional packages provide outbound HTTP stubs, message recording, Verify.Xunit integration, and isolated Azure Functions helpers.

## Packages

- `XBullet.EasyTesting` provides the authenticated ASP.NET Core test host and test-user clients.
- `XBullet.EasyTesting.EntityFrameworkCore` provides generic scoped database actions and an EF-backed test factory.
- `XBullet.EasyTesting.Http` provides fluent outbound HTTP stubs and request recording.
- `XBullet.EasyTesting.Messaging` provides transport-neutral published-message recording.
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

These profiles test controller authentication and authorization without external identity infrastructure. They intentionally bypass signature, issuer, token-expiry, and secret validation. To integration-test the real authentication handler instead, do not map its scheme; send its actual credential with `Client().WithHeader(...)`.

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
    .RespondJson(new { Id = 701, Name = "Keyboard", Price = 149.95m });

using var client = factory.Client()
    .AsUser(user => user.WithName("Importer"))
    .Build();

using var response = await client.PostAsync("/api/products/import/701", null);

var savedProduct = await factory.QueryDatabaseAsync(
    (database, cancellationToken) => database.Products
        .SingleAsync(product => product.Id == 701, cancellationToken));

Assert.Equal(HttpStatusCode.Created, response.StatusCode);
Assert.Single(factory.ExternalCatalog.Requests);
```

`Respond`, `RespondJson`, and `RespondText` cover empty, JSON, and text responses. Every call is captured in `Requests`, including its method, URI, headers, and body. An unmatched request receives `501 Not Implemented` with a diagnostic message, making missing arrangements visible without network access.

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

var published = Assert.Single(
    factory.PublishedMessages.For(MessageTransportNames.Kafka, "orders.created"));
var message = published.GetPayload<OrderCreatedMessage>();

Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
Assert.Equal("customer-7", published.Headers["partition-key"]);
Assert.Equal(42, message!.OrderId);
```

Well-known names are included for Kafka, Azure Service Bus, and Azure Notification Hubs. `RecordAsync` also accepts any custom transport or destination, so the same pattern covers RabbitMQ, Event Hubs, SNS/SQS, email, webhooks, or application-specific notification providers. Payloads and headers are copied at publication time to prevent later mutation from changing assertions.

## Azure Functions isolated worker

Reference `XBullet.EasyTesting.AzureFunctions` to resolve function classes from a test service provider and invoke HTTP, timer, and Kafka entry points directly:

```csharp
var recorder = new RecordingTriggerInvocationSink();
await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<ProcessOrderHttpFunction>()
    .AddFunction<CleanupTimerFunction>()
    .AddFunction<ProcessOrderKafkaFunction>()
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

Timer and Kafka trigger values use the same fluent/direct style:

```csharp
var timer = AzureFunctionTestHost.Timer()
    .PastDue()
    .WithSchedule(last, next)
    .Build();

var kafkaMessage = KafkaTriggerData.Json(
    new KafkaOrderMessage("order-42", 3));
```

These tests exercise function code, dependency injection, serialization, and output behavior without Azure Functions Core Tools, storage, or a Kafka broker. They do not validate host indexing, binding expressions, broker connectivity, checkpoints, retries, or deployment configuration; keep a smaller runtime-level test suite for those concerns.

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

GitHub Actions runs restore, formatting validation, build, tests, and package creation for pushes and pull requests. To publish packages, add a scoped NuGet.org API key as the `NUGET_API_KEY` repository secret, update `CHANGELOG.md`, and publish a GitHub Release with a semantic-version tag such as `v0.1.0`. The release workflow publishes all `XBullet.EasyTesting.*` packages and their symbol packages.

### Preview flow

Every CI run creates preview packages using the current `VersionPrefix` and the workflow run number, for example `0.1.0-preview.42`. Download the `nuget-preview-42` workflow artifact and use its directory as a local NuGet source to test the complete package set without publishing it.

To publish a public preview to NuGet.org, create a GitHub Release with a tag such as `v0.2.0-preview.1` and select **Set as a pre-release**. The release workflow verifies that the GitHub release type and semantic version agree before publishing. Install public previews with:

```shell
dotnet add package XBullet.EasyTesting --prerelease
```

For a stable release, use a tag without a suffix, such as `v0.2.0`, and do not mark the GitHub Release as a pre-release.

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and [SECURITY.md](SECURITY.md) for private vulnerability reporting.
