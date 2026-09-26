# External APIs, databases, and messaging

This guide shows how an integration-test scenario can control external HTTP dependencies, database state, and published messages. Package-specific guides remain the source for exact package installation and focused examples.

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

When `XBullet.EasyTesting.Snapshots.Http` is referenced, snapshot every complete request/response
exchange captured by the handler with the same update, scrubber, acceptance, and diff-viewer
workflow used by controller snapshots:

```csharp
await stub.ShouldMatchExchangesSnapshot();
```

Exchange snapshots contain response status, stable headers, and structural JSON, text, or binary
bodies in addition to the request. Send failures, partial content, and content-read failures are
also recorded. Response bodies are observed as the application reads them rather than consumed
eagerly; unread content appears as `{NotRead}`.

Request and response capture options and the complete-exchange file format are configured through
`StubHttpExchangeSnapshotOptions`:

```csharp
var exchangeOptions = new StubHttpExchangeSnapshotOptions
{
    Format = HttpExchangeSnapshotFormat.Yaml // Json (default), Http, or Yaml
};

await stub.ShouldMatchExchangesSnapshot(exchangeOptions);
```

HTTP output uses `.verified.txt`; YAML output uses `.verified.yaml`. Structured scrubbers are
applied before rendering either format.

Request-only snapshots remain JSON and can be configured independently:

```csharp
await stub.ShouldMatchRequestsSnapshot(
    new StubHttpRequestSnapshotOptions()
        .IgnoringHeaders("X-Request-Nonce")
        .RedactingHeader("X-Session")
        .RedactingQueryParameter("tenant_secret"),
    new SnapshotSettings()
        .ScrubMembers("timestamp", "requestId"));
```

JSON request bodies are captured structurally. Authorization, cookies, API keys, correlation IDs,
and tracing headers are excluded by default. Common secret-bearing query parameters are redacted,
and additional headers or query parameters can be explicitly redacted. Use
`ShouldMatchRequestSnapshot` on an individual `StubHttpRequest`.

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
