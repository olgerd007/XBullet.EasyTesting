# Run end-to-end testing flows

Follow a complete test from host configuration to request, observable result, diagnostics, and
cleanup. This guide brings together executable flows for CRUD, outbound HTTP, published events,
real authentication handlers, snapshots, Azure Functions, and distributed applications.

Choose the boundary you need to exercise before choosing a host. Here, an application flow runs
through the application's entry point and real application code with explicitly controlled
dependencies. A distributed flow starts real application processes. Neither path drives a browser
or validates a deployed environment unless you add that coverage separately.

## Choose a flow

| Flow | Entry point and result | Controlled boundary | Packages beyond core |
| --- | --- | --- | --- |
| [CRUD](#create-read-update-and-delete-a-product) | HTTP requests change and retrieve persisted state | SQLite database per scenario; simulated identity | EntityFrameworkCore |
| [External API](#import-from-an-external-api-and-handle-failure) | HTTP request calls a typed client and persists its response | Outbound HTTP handler and scenario database | EntityFrameworkCore, Http |
| [Published event](#publish-and-verify-an-integration-event) | HTTP request produces a routed message | Publisher records instead of sending to a broker | Messaging |
| [Credential validation](#validate-credentials-through-the-real-handler) | Signed JWT reaches the application's bearer handler and policy | Local issuer and keys | None |
| [HTTP contract](#capture-and-review-an-http-contract) | Real request and response match a reviewed baseline | Capture options, redaction, and baseline files | Snapshots.Core |
| [Functions](#invoke-a-function-with-application-services) | Constructed Kafka trigger invokes a function and its application services | Pricing HTTP and invocation recorder | AzureFunctions, Http |
| [Distributed application](#start-a-distributed-application) | AppHost starts a service and a health request reaches its endpoint | AppHost test configuration | Aspire |

These are independent flows, not a sequence of dependent tests. Combine them only when one business
operation owns the behavior being asserted. For a single request that combines authorization,
external data, persistence, and event publication, follow the
[complete order workflow](production-workflows.md).

## Set up and run the examples

From a repository checkout, install the SDK selected by `global.json` and the ASP.NET Core runtimes
for .NET 8, .NET 9, and .NET 10. The in-process flows need no external account or broker:

```shell
dotnet restore XBullet.EasyTesting.sln
dotnet build XBullet.EasyTesting.sln --configuration Release --no-restore
dotnet test tests/TestApi.IntegrationTests/TestApi.IntegrationTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/TestFunctions.IntegrationTests/TestFunctions.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

Without `--framework`, each test project runs all three supported target frameworks. The ASP.NET
Core project also contains explicit Aspire tests; those require the separate command below.

In a consuming application, reference your application from its test project and install the
packages selected by the table above. For example, an outbound HTTP and persistence flow uses:

```shell
dotnet add reference ../MyApi/MyApi.csproj
dotnet add package xunit.v3
dotnet add package XBullet.EasyTesting
dotnet add package XBullet.EasyTesting.EntityFrameworkCore
dotnet add package XBullet.EasyTesting.Http
```

Expose `public partial class Program;` in a minimal-host application. Use a Startup host for an
existing `Startup` application. Follow [host selection](../concepts/test-hosts.md) for the exact
entry point and factory hooks; production application projects do not need EasyTesting references.

The examples below reuse repository fixtures and domain models. `TestApiFactory`,
`EndToEndAuthenticationFactory`, `OrderFunctionFactory`, `BuiltInSnapshotAudit`, and the application
DTOs are sample-project types, not framework APIs. Code blocks are generated from the linked source
regions. Method excerpts omit their containing classes and `using` directives; complete files are
linked alongside them.

## Follow the framework lifecycle

Every in-process application flow has the same ownership sequence:

1. **Configure the factory.** Select the entry point, authentication mode, database provider, and
   service replacements before the host resolves those dependencies.
2. **Open a scenario.** Acquire the factory's scenario gate and create the scenario's host and
   service provider. Registered resources participate in setup and cleanup.
3. **Arrange inside that scenario.** Seed its database and configure the exact stub responses the
   application will consume. Use the same scope for database setup, requests, and queries.
4. **Send the request or invoke the function.** Pass the test cancellation token through I/O. For
   several requests in one journey, keep one scenario scope and use a client or a fresh request
   builder for each request; a fluent scenario builder represents one request.
5. **Verify observable behavior.** Assert the response, stored state, and relevant outbound calls or
   messages. Rejected requests should also prove that unwanted side effects did not happen.
6. **Capture diagnostics and clean up.** A scenario runner captures registered resource diagnostics
   before cleanup on failure or cancellation. Dispose clients, responses, and results with `using`.
   The fixture owns its factory; each scenario owns its scoped host and scoped services.

The runner protects shared mutable recorders within one factory. It does not make unrelated
factories share a lock or isolate a shared external database automatically. See
[scenario isolation](../concepts/scenarios-and-isolation.md) and
[resource ownership](../concepts/resources-and-cleanup.md) for those contracts.

## Create, read, update, and delete a product

**Flow:** scenario database -> authenticated client -> create -> read -> update -> delete -> database
verification.

The repository's [TestApiFactory](../../tests/TestApi.IntegrationTests/TestApiFactory.cs) uses a
separate in-memory SQLite connection for each scenario and registers that connection for scenario
disposal. Its HTTP stub and message recorder are registered scenario resources. The xUnit fixture
owns the factory; this example explicitly owns its scope, client, and responses.

<!-- snippet: tests/TestApi.IntegrationTests/DocumentationExamples.cs#docs-authenticated-crud -->
```csharp
public sealed class AuthenticatedCrudDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public AuthenticatedCrudDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_client_can_create_read_update_and_delete_a_product()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var client = scope.Client()
            .AsUser(user => user.WithName("CRUD tester"))
            .Build();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/products",
            new ProductRequest("Webcam", 79.95m),
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var resourceUri = $"/api/products/{created.Id}";
        using var readResponse = await client.GetAsync(resourceUri, cancellationToken);
        var read = await readResponse.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(created, read);

        using var updateResponse = await client.PutAsJsonAsync(
            resourceUri,
            new ProductRequest("Conference webcam", 99.95m),
            cancellationToken);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductResponse>(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Conference webcam", updated!.Name);

        using var deleteResponse = await client.DeleteAsync(resourceUri, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var remainingProducts = await _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);
        Assert.Equal(0, remainingProducts);
    }

    private sealed record ProductRequest(string Name, decimal Price);

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
```
<!-- end-snippet -->

Expected results are `201`, `200`, `200`, and `204`, followed by zero persisted products. Keep these
operations in one scope because each operation consumes state produced by the preceding one.
Keep unrelated tests independent of this journey.

The scope is disposed on an assertion failure, but direct scope use does not provide the runner's
automatic failure-diagnostic capture. Prefer `RunInTestScenarioScopeAsync` or `ScopedTest.RunAsync`
when recorded diagnostics matter, as in the next flow. SQLite exercises relational behavior but
does not establish compatibility with a different production database provider. Continue with the
[EF Core guide](entity-framework-core.md) for provider selection and lifecycle options.

## Import from an external API and handle failure

**Flow:** authenticated import request -> real typed HTTP client -> stubbed remote response ->
SQLite write -> API response and stored-row verification.

`TestApiFactory` replaces the typed client's primary HTTP handler with `ExternalCatalog`; the typed
client's request building and JSON handling remain active. In the
[complete test class](../../tests/TestApi.IntegrationTests/ExternalApiControllerTests.cs), `Run`
calls `_factory.RunInTestScenarioScopeAsync` with `TestContext.Current.CancellationToken`.

<!-- snippet: tests/TestApi.IntegrationTests/ExternalApiControllerTests.cs#docs-http-controller-integration -->
```csharp
[Fact]
public Task Controller_calls_external_api_and_saves_the_result_to_database() =>
    Run(async (scope, cancellationToken) =>
    {
        _factory.ExternalCatalog
            .When(HttpMethod.Get, "/products/701")
            .RespondJson(new { Id = 701, Name = "External Keyboard", Price = 149.95m });
        using var client = scope.Client()
            .AsUser(TestUser.Create(name: "Importer"))
            .Build();

        using var response = await client.PostAsync(
            "/api/products/import/701",
            content: null,
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);
        var persisted = await _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products
                .AsNoTracking()
                .SingleAsync(product => product.Id == 701, token),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new ProductResponse(701, "External Keyboard", 149.95m), body);
        Assert.Equal("External Keyboard", persisted.Name);
        Assert.Equal(149.95m, persisted.Price);
        var request = Assert.Single(_factory.ExternalCatalog.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/products/701", request.RequestUri!.PathAndQuery);
    });
```
<!-- end-snippet -->

The test verifies the response body, persisted values, and exactly one request to `/products/701`.
This is a transport-level stub: no request reaches a remote server. The application still exercises
its real typed client, unlike a service-interface replacement.

For the failure branch, arrange a remote `503`. The application must return `502` and save no
product:

<!-- snippet: tests/TestApi.IntegrationTests/ExternalApiControllerTests.cs#docs-http-workflow-failure -->
```csharp
[Fact]
public Task External_failure_returns_bad_gateway_without_saving_a_product() =>
    Run(async (scope, cancellationToken) =>
    {
        _factory.ExternalCatalog
            .When(HttpMethod.Get, "/products/703")
            .Respond(HttpStatusCode.ServiceUnavailable);
        using var client = scope.CreateAuthenticatedClient();

        using var response = await client.PostAsync(
            "/api/products/import/703",
            content: null,
            cancellationToken);
        var productCount = await CountProductsAsync(scope, cancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, productCount);
        Assert.Equal(1, _factory.ExternalCatalog.CallCount);
    });
```
<!-- end-snippet -->

A mismatched or unarranged request is a setup error, not an automatic remote success. Inspect the
recorded method, path, query, and body before changing an expectation. The same class covers missing
products and anonymous requests. See [outbound HTTP](outbound-http.md) for exact matchers,
sequences, transport exceptions, delays, cancellation, and verification APIs.

## Publish and verify an integration event

**Flow:** authenticated order request -> controller and application publisher -> recorded destination,
headers, and typed event payload.

The [factory](../../tests/TestApi.IntegrationTests/TestApiFactory.cs) replaces
`IApplicationMessagePublisher` with a recording adapter backed by `RecordedMessageBus`. In the
[publishing test class](../../tests/TestApi.IntegrationTests/PublishingControllerTests.cs), `Run`
uses the scenario runner and `CreateAuthenticatedClient(scope)` builds a simulated `Publisher` user.

<!-- snippet: tests/TestApi.IntegrationTests/PublishingControllerTests.cs#docs-kafka-publishing -->
```csharp
[Fact]
public Task Controller_publishes_order_to_kafka_topic() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = CreateAuthenticatedClient(scope);
        using var response = await client.PostAsJsonAsync(
            "/api/publishing/kafka/orders",
            new { OrderId = 42, CustomerId = "customer-7", Total = 125.50m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var recorded = Assert.Single(
            _factory.PublishedMessages.For(MessageTransportNames.Kafka, "orders.created"));
        Assert.Equal("order-created", recorded.Headers["event-type"]);
        Assert.Equal("customer-7", recorded.Headers["partition-key"]);
        Assert.Equal(
            new OrderCreatedMessage(42, "customer-7", 125.50m),
            recorded.GetPayload<OrderCreatedMessage>());
    });
```
<!-- end-snippet -->

The expected result is `202` and one `orders.created` event with the customer's partition key.
This establishes the application's publication contract, not Kafka delivery, acknowledgement, or
consumer processing. The same class verifies that invalid input publishes nothing. If publication
runs in background work, use [eventual assertions](eventual-assertions.md) with a bounded timeout;
do not wait an arbitrary fixed delay. See [messaging](messaging.md) for transport and payload checks.

## Validate credentials through the real handler

**Flow:** locally signed JWT -> Authorization header -> real bearer validation -> authorization
policy -> endpoint.

Simulated `AsUser` and `AsAzureAdUser` identities exercise authorization without validating a token.
For a credential-validation flow, configure the corresponding production handler for a test
authority. This is the repository's
[authentication factory](../../tests/TestApi.IntegrationTests/EndToEndAuthenticationFactory.cs):

<!-- snippet: tests/TestApi.IntegrationTests/EndToEndAuthenticationFactory.cs#docs-end-to-end-authentication-factory -->
```csharp
public sealed class EndToEndAuthenticationFactory : TestApiFactory
{
    protected override void ConfigureTestAuthentication(TestAuthenticationSchemeBuilder authentication)
    {
        authentication
            .UseEndToEndJwt("AzureAd", authority => authority
                .WithIssuer("https://identity.xbullet.test")
                .WithAudience("test-api")
                .WithTokenLifetime(TimeSpan.FromMinutes(2))
                .WithClockSkew(TimeSpan.Zero)
                .SaveAccessToken())
            .UseEndToEndJwt("PartnerBearer", authority => authority
                .WithIssuer("https://partner.xbullet.test")
                .WithAudience("test-api-partner")
                .WithDiscoveryPath("/.well-known/partner/openid-configuration")
                .WithJwksPath("/.well-known/partner/jwks.json")
                .WithoutDefaultScheme())
            .UseEndToEndApiKey("ApiKey", apiKey => apiKey
                .WithHeaderName("X-Api-Key")
                .WithQueryParameterName("api_key"))
            .UseEndToEndClientCertificate("Certificate");
    }
}
```
<!-- end-snippet -->

The factory derives from `TestApiFactory`. Its `AzureAd` scheme uses a local JWT authority, while
the other registrations demonstrate a named partner authority, an API-key handler, and a client
certificate handler. Your application needs only the registrations it uses.

In the [complete authentication test class](../../tests/TestApi.IntegrationTests/EndToEndAuthenticationTests.cs),
`Run` delegates to the same isolated scenario runner, using this factory:

<!-- snippet: tests/TestApi.IntegrationTests/EndToEndAuthenticationTests.cs#docs-end-to-end-jwt -->
```csharp
[Fact]
public Task Locally_signed_jwt_is_validated_by_the_real_bearer_handler() =>
    Run(async (scope, cancellationToken) =>
    {
        using var result = await scope.Scenario()
            .AsJwt(token => token
                .WithSubject("user-42")
                .WithName("Ada")
                .WithClaim("tid", "tenant-42")
                .WithScope("orders.read")
                .ExpiresAfter(TimeSpan.FromMinutes(1)))
            .Get("/api/secure/azure-ad")
            .ExecuteAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, result.Response.StatusCode);
    });
```
<!-- end-snippet -->

The valid JWT returns `204`. The same test class checks `401` for expired, malformed, wrong-audience,
wrong-issuer, invalid-signature, unknown-key, unsigned, and not-yet-valid tokens. Those failure cases
also check authentication-event diagnostics for validation failure and challenge.

The authority and signing keys are local test data. This exercises the configured handler without
contacting a live identity provider; it does not establish that deployed issuer configuration or
network access is correct. For setup, scheme selection, API keys, and certificates, see
[authentication models](../concepts/authentication.md).

## Capture and review an HTTP contract

**Flow:** recorder installed before the request -> product creation -> captured request and response
-> stable baseline comparison -> review of any received file.

In [TestApiFactory](../../tests/TestApi.IntegrationTests/TestApiFactory.cs), `SnapshotClient(scope, ... )`
adds an `HttpExchangeRecorder` to the client. The
[snapshot test class](../../tests/TestApi.IntegrationTests/CrudControllerSnapshotTests.cs) supplies
the `ProductRequest` record and scenario runner. `BuiltInSnapshotAudit.CreateSettings` registers
snapshots with the repository's audit catalog; it is not required by the framework. Caller-source
defaults locate the baseline beside the test source.

<!-- snippet: tests/TestApi.IntegrationTests/CrudControllerSnapshotTests.cs#docs-snapshots-complete-exchange -->
```csharp
[Fact]
public Task Create_matches_custom_snapshot_with_generated_id_scrubbed() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = _factory.SnapshotClient(
                scope,
                options => options.Response.IgnoringHeaders("Location"))
            .AsUser(user => user.WithName("CRUD snapshot tester"))
            .Build();

        using var response = await client.PostAsJsonAsync(
            "/api/products",
            new ProductRequest("Webcam", 79.95m),
            cancellationToken);

        await response.ShouldMatchHttpExchangeSnapshot(
            snapshotSettings: BuiltInSnapshotAudit.CreateSettings(
                settings => settings.ScrubMember("id")),
            cancellationToken: cancellationToken);
    });
```
<!-- end-snippet -->

The generated resource ID is scrubbed and the unstable `Location` header is excluded. The assertion
compares the captured request and response with a committed baseline. A mismatch writes a received
file and fails; review the difference before accepting an intentional contract change.

For your application, configure `SnapshotSettings` directly or use a project helper. Install the
recorder before sending the request, apply redaction before capture where possible, and keep baseline
updates as an explicit developer action. Assertion options can further transform a recorded copy;
they cannot recover data excluded or redacted during capture. The
[snapshot getting-started guide](snapshots/getting-started.md) explains file placement, initial
baseline creation, review, and CI behavior.

## Invoke a function with application services

**Flow:** application `Program` configuration -> test service overrides -> constructed Kafka trigger
-> real function and pricing client -> recorded application result.

This flow reuses application DI and function code through `AzureFunctionTestFactory<Program>`. The
[factory definition](azure-functions.md#reuse-programcs-with-a-factory) in the Functions guide replaces
the pricing HTTP handler and invocation sink. The complete
[factory test file](../../tests/TestFunctions.IntegrationTests/AzureFunctionTestFactoryTests.cs)
contains those helper types and imports:

<!-- snippet: tests/TestFunctions.IntegrationTests/AzureFunctionTestFactoryTests.cs#docs-functions-program-factory -->
```csharp
[Fact]
public async Task Factory_reuses_program_configuration_and_services_with_test_overrides()
{
    await using var factory = new OrderFunctionFactory();
    var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
    factory.Pricing.When(HttpMethod.Get, "/products/42/price")
        .RespondJson(new ProductPrice(42, 19.95m, "USD"));

    await host.RunInTestScenarioScopeAsync(async (scope, token) =>
    {
        var trigger = KafkaTriggerData.JsonTrigger(
            new OrderPricingRequestedMessage("order-42", 42, 3), topic: "order-pricing");
        await scope.Host.InvokeAsync<PriceOrderKafkaFunction, string>(
            nameof(PriceOrderKafkaFunction), trigger,
            (function, message, context) => function.RunAsync(message, context), token);

        Assert.Equal(59.85m, Assert.Single(factory.Recorder.Invocations).Amount);
        factory.Pricing.VerifyCalled(HttpMethod.Get, "/products/42/price");
    }, TestContext.Current.CancellationToken);
}
```
<!-- end-snippet -->

The result is one priced invocation for `59.85` and a verified HTTP pricing lookup. This directly
invokes a .NET isolated function with constructed trigger context. It does not start the Azure
Functions worker runtime, receive a broker message, or validate delivery and acknowledgement.
The factory is asynchronously disposed; the scenario runner owns its invocation scope and resets
the registered recorder. See the [Functions host guide](azure-functions.md) and
[trigger recipes](azure-functions-triggers.md) for binding and middleware coverage.

## Start a distributed application

**Flow:** AppHost startup -> required resource health -> discovered HTTP endpoint -> service request
-> failure diagnostics -> shutdown.

Use `AspireTestHost` when application processes or a resource topology are part of the flow. The
repository's runnable example below is explicit and available on `net10.0`. Its AppHost project is
referenced using the `testapphost` alias, declared at the top of the
[complete Aspire test file](../../tests/TestApi.IntegrationTests/AspireTests.cs).

<!-- snippet: tests/TestApi.IntegrationTests/AspireTests.cs#docs-aspire-run-diagnostics -->
```csharp
[Fact(Explicit = true)]
public async Task Distributed_app_waits_for_health_serves_http_and_attaches_failure_diagnostics()
{
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
            .WithArguments("--environment=Testing")
            .ConfigureAppHost(_ => { })
            .WaitForResource("api")
            .WithStartupTimeout(TimeSpan.FromMinutes(2))
            .RunAsync(
                async (application, cancellationToken) =>
                {
                    Assert.Contains("api", application.ResourceNames);
                    Assert.NotNull(application.GetEndpoint("api"));
                    using var client = application.CreateHttpClient("api");
                    using var response = await client.GetAsync("/health", cancellationToken);
                    response.EnsureSuccessStatusCode();
                    Assert.Contains(
                        "Healthy",
                        await response.Content.ReadAsStringAsync(cancellationToken));
                    throw new InvalidOperationException("Expected distributed test failure.");
                },
                TestContext.Current.CancellationToken));

    var diagnostics = Assert.IsType<AspireApplicationDiagnostics>(
        exception.Data[AspireApplicationDiagnostics.ExceptionDataKey]);
    var api = Assert.Single(diagnostics.Resources, resource => resource.Name == "api");
    Assert.Equal("Healthy", api.HealthStatus);
    Assert.NotEmpty(api.Logs);
}
```
<!-- end-snippet -->

The example deliberately throws after a successful health request to verify attached diagnostics.
It passes when the expected failure contains healthy resource state and logs. In an application
journey, put your service request and business assertions in the `RunAsync` callback; the runner
owns startup and shutdown, including failure cleanup.

Run this class separately after building the repository, with the required Aspire runtime available.
When running a filtered class in CI, disable the full-suite snapshot audit for that invocation:

```powershell
$env:XBULLET_SKIP_SNAPSHOT_AUDIT = "true"
dotnet test tests/TestApi.IntegrationTests/TestApi.IntegrationTests.csproj --configuration Release --framework net10.0 --no-build --no-restore --filter-class TestApi.IntegrationTests.AspireTests --explicit on
Remove-Item Env:XBULLET_SKIP_SNAPSHOT_AUDIT
```

The sample AppHost launches its API project; Docker is needed when your topology adds container
resources. Declare all required resource health waits and bound startup with a timeout. The
[Aspire guide](aspire.md) describes discovery, diagnostics, and manual lifecycle control. For a
single-service test with a real database or broker, use the
[Testcontainers flow](testcontainers.md) instead of starting an entire distributed topology.

## Diagnose and maintain a flow

| Failure | Check first | What to verify before retrying |
| --- | --- | --- |
| Unexpected `401` or `403` | Selected authentication mode, scheme, roles, and scopes | Whether credentials should be validated or an identity simulated |
| Missing or unexpected outbound call | Stub's recorded method, URI, and arrangement | Whether the right typed client uses the configured handler |
| Wrong persisted state | Database query's scenario scope | Whether all requests and setup used that same scope |
| Missing event | HTTP result, destination, payload, and recorder diagnostics | Whether publication completed synchronously or needs bounded eventual assertions |
| Snapshot mismatch | Received versus verified file and redaction rules | Whether the change is intentional and safe to commit |
| Distributed startup timeout | Attached resource state and logs | Whether dependencies are runnable and required health checks can succeed |

Keep failures observable: an expected negative flow should assert its status and absence of unwanted
rows, calls, or messages. Propagate the test cancellation token; let the runner collect diagnostics
and clean up after cancellation. Do not blindly retry a state-changing request against a previous
test's state.

Run the ordinary solution build and tests in CI. Add explicit distributed or container jobs with
their own prerequisites, timeouts, and artifact policy. Keep credentials and personal data out of
recorded requests, snapshots, and uploaded diagnostics; follow
[sensitive-data guidance](../concepts/security-and-sensitive-data.md).

When changing an example, edit its canonical test source and run
`eng/sync-documentation-snippets.ps1`. The ordinary documentation gate checks that the guide still
matches its executable source. See
[documentation examples](../contributing/documentation-examples.md) for the authoring workflow and
the [API index](../api/index.md) for the entry-point contracts used throughout these flows.
