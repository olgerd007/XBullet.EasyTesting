# Test outbound HTTP dependencies

Use `XBullet.EasyTesting.Http` when application code must call an HTTP dependency but the test
should stay deterministic and offline. `StubHttpMessageHandler` is a real `HttpMessageHandler`, so
the application still exercises its typed or named `HttpClient`, serialization, and error handling.

## Install and connect the stub

```shell
dotnet add package XBullet.EasyTesting.Http
```

Import `XBullet.EasyTesting.Http`; hosted examples also use `XBullet.EasyTesting.Hosting`.

Replace the primary handler in the test factory and reuse the same handler instance so the test can
arrange responses and inspect calls:

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

Register a shared handler as a scenario resource when the factory is reused. Its requests,
exchanges, and response positions are then reset between scenarios and its diagnostics are captured
before cleanup. See [resources and cleanup](../concepts/resources-and-cleanup.md).

## Minimal request and response

Arrange a response, send through an ordinary `HttpClient`, and inspect both the request and the
completed exchange:

<!-- snippet: tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs#docs-http-basic-exchange -->
```csharp
[Fact]
public async Task Arranged_response_is_returned_and_request_is_recorded()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    using var handler = new StubHttpMessageHandler();
    handler
        .When(HttpMethod.Post, "/orders?notify=true")
        .RespondJson(new { Accepted = true }, HttpStatusCode.Accepted);
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://external.example.test/")
    };
    client.DefaultRequestHeaders.Add("X-Tenant", "tenant-42");

    using var response = await client.PostAsJsonAsync(
        "/orders?notify=true",
        new { OrderId = 42 },
        cancellationToken);
    var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.True(body!.Accepted);
    var request = Assert.Single(handler.Requests);
    Assert.Equal(HttpMethod.Post, request.Method);
    Assert.Equal("/orders?notify=true", request.RequestUri!.PathAndQuery);
    Assert.Equal(["tenant-42"], request.Headers["X-Tenant"]);
    Assert.Contains("\"orderId\":42", request.Body);

    var exchange = Assert.Single(handler.Exchanges);
    Assert.Same(request, exchange.Request);
    Assert.Null(exchange.Failure);
    Assert.NotNull(exchange.Response);
    Assert.Equal((int)HttpStatusCode.Accepted, exchange.Response.StatusCode);
    Assert.True(exchange.Response.BodyCaptured);
    Assert.Contains("\"accepted\":true", Encoding.UTF8.GetString(exchange.Response.Body.Span));
    Assert.Null(exchange.Response.BodyFailure);
}
```
<!-- end-snippet -->

`Requests` contains the buffered method, URI, headers, and body. `Exchanges` additionally records
the response or send failure. Response content is observed when the application reads it; the stub
does not eagerly consume a streaming body.

## Bound retained HTTP data

The default handler retains every exchange and complete request and consumed response bodies. For
long-running or payload-heavy tests, configure explicit limits when constructing the handler:

```csharp
using var handler = new StubHttpMessageHandler(new StubHttpMessageHandlerOptions
{
    MaximumRecordedExchanges = 500,
    MaximumRequestBodyBytes = 64 * 1024,
    MaximumResponseBodyBytes = 64 * 1024
});
```

`CallCount` still counts every received request, while `Requests` and `Exchanges` expose the newest
retained entries. Set `MaximumRecordedExchanges` to zero to disable retention. A truncated captured
body sets `BodyTruncated` on the request, response, and corresponding snapshot representation.
`CaptureRequestBodies` and `CaptureResponseBodies` can disable body retention independently.

Request matchers and response factories receive the configured captured request body. Do not
truncate or disable request-body capture when a rule depends on content beyond the retained bytes.

## Match the request precisely

Start a rule with `When(method, pathOrUri)`. Add any combination of:

- `WithQueryParameter` for decoded, repeated, order-independent query values or a value predicate.
- `WithRequestHeader` for a required header value.
- `WithRequestBody` for exact text.
- `WithJsonRequestBody` for structural JSON equality.
- `WithJsonProperty` or `WithJsonPath` for selected JSON values and predicates. Paths support
  dot-separated properties and zero-based array indexes.
- `WithRequest` for a custom `StubHttpRequest` predicate and a diagnostic description.

This executable example combines a header, structural JSON, a dynamic response, and verification:

<!-- snippet: tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs#docs-http-matchers -->
```csharp
[Fact]
public async Task Request_matchers_support_headers_json_and_dynamic_responses()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    using var handler = new StubHttpMessageHandler();
    handler
        .When(HttpMethod.Post, "/orders")
        .WithRequestHeader("X-Tenant", "tenant-42")
        .WithJsonRequestBody(new { OrderId = 42 })
        .Respond(request => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new { CapturedBody = request.Body })
        });
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://external.example.test/")
    };
    client.DefaultRequestHeaders.Add("X-Tenant", "tenant-42");

    using var response = await client.PostAsJsonAsync(
        "/orders",
        new { OrderId = 42 },
        cancellationToken);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    handler
        .VerifyCalled(HttpMethod.Post, "/orders")
        .Verify(
            request => request.Headers.ContainsKey("X-Tenant"),
            expectedCount: 1,
            description: "a request containing the tenant header");
}
```
<!-- end-snippet -->

A rule without a query matcher ignores the query. Add explicit query matchers when those values are
part of the contract. Prefer structural JSON matching over exact text when whitespace and property
order are irrelevant.

## Response sequences and retry behavior

Use `Respond`, `RespondJson`, or `RespondText` for fixed output, or callbacks and `RespondAsync` to
derive output from the captured request. A sequence consumes exactly one entry per matching call:

<!-- snippet: tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs#docs-http-sequence -->
```csharp
[Fact]
public async Task Response_sequence_returns_each_response_once_and_then_fails_loudly()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    using var handler = new StubHttpMessageHandler();
    handler
        .When(HttpMethod.Get, "/status")
        .RespondSequence(sequence => sequence
            .Respond(HttpStatusCode.ServiceUnavailable)
            .RespondJson(new { Accepted = true }, HttpStatusCode.OK));
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://external.example.test/")
    };

    using var first = await client.GetAsync("/status", cancellationToken);
    using var second = await client.GetAsync("/status", cancellationToken);
    var secondBody = await second.Content.ReadFromJsonAsync<Response>(cancellationToken);
    var exception = await Assert.ThrowsAsync<StubHttpSequenceExhaustedException>(
        () => client.GetAsync("/status", cancellationToken));

    Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    Assert.True(secondBody!.Accepted);
    Assert.Contains("contains 2 response(s), but call 3 was received", exception.Message);
}
```
<!-- end-snippet -->

Sequence exhaustion throws `StubHttpSequenceExhaustedException`; it does not silently repeat the
last response. That makes an unexpected retry visible. `WithDelay` can delay a rule or an individual
sequence entry.

## Failures, cancellation, timeouts, and bad content

Use `Throw` for transport exceptions, `Cancel` or `CancelAfter` for cancellation, and
`TimeoutAfter` for a deterministic `TimeoutException`. `Timeout()` waits until the caller's token or
`HttpClient.Timeout` cancels the operation. These behaviors can be mixed in a response sequence:

<!-- snippet: tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs#docs-http-faults -->
```csharp
[Fact]
public async Task Response_sequence_supports_exceptions_cancellation_and_timeouts()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    using var handler = new StubHttpMessageHandler();
    handler
        .When(HttpMethod.Get, "/sequence")
        .RespondSequence(sequence => sequence
            .Throw(_ => new InvalidOperationException("sequence failure"))
            .Cancel()
            .CancelAfter(TimeSpan.FromMilliseconds(5))
            .TimeoutAfter(TimeSpan.FromMilliseconds(5))
            .Timeout());
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://external.example.test/")
    };

    var failure = await Assert.ThrowsAsync<InvalidOperationException>(
        () => client.GetAsync("/sequence", cancellationToken));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => client.GetAsync("/sequence", cancellationToken));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => client.GetAsync("/sequence", cancellationToken));
    var timeout = await Assert.ThrowsAsync<TimeoutException>(
        () => client.GetAsync("/sequence", cancellationToken));
    using var timeoutCancellation =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCancellation.CancelAfter(TimeSpan.FromMilliseconds(25));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => client.GetAsync("/sequence", timeoutCancellation.Token));

    Assert.Equal("sequence failure", failure.Message);
    Assert.Contains("timed out after", timeout.Message);
}
```
<!-- end-snippet -->

`RespondMalformedJson` returns invalid JSON, while `RespondTruncated` returns headers and partial
content before failing during the read. Use them to verify that application code distinguishes a
bad payload from a failed send.

## A realistic controller and database test

The following integration test arranges the external catalog, calls an authenticated controller,
asserts its response, verifies persisted state, and checks the outbound call:

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

This is the useful boundary: the test replaces the remote network, not the application's client or
business workflow. Combine it with the [EF Core guide](entity-framework-core.md) when the result is
persisted.

## Verification and failure diagnostics

Use `VerifyCalled`, `VerifyNotCalled`, or `Verify(predicate, expectedCount, description)` for
interaction assertions. An unmatched call returns `501 Not Implemented`; its body lists why each
configured rule failed:

<!-- snippet: tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs#docs-http-diagnostics -->
```csharp
[Fact]
public async Task Unmatched_request_explains_why_each_rule_failed()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    using var handler = new StubHttpMessageHandler();
    handler
        .When(HttpMethod.Post, "/orders")
        .Respond(HttpStatusCode.Created)
        .When(HttpMethod.Get, "/customers")
        .Respond(HttpStatusCode.OK)
        .When(HttpMethod.Get, "/orders")
        .WithQueryParameter("page", "2")
        .WithRequestHeader("X-Tenant", "tenant-42")
        .Respond(HttpStatusCode.OK);
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://external.example.test/")
    };

    using var response = await client.GetAsync("/orders?page=3", cancellationToken);
    var diagnostic = await response.Content.ReadAsStringAsync(cancellationToken);

    Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    Assert.Contains("Rule 1 (POST /orders): method differed", diagnostic);
    Assert.Contains("Rule 2 (GET /customers): URI differed", diagnostic);
    Assert.Contains(
        "Rule 3 (GET /orders): did not satisfy query parameter 'page' containing value '2'",
        diagnostic);
    Assert.Contains("did not satisfy header 'X-Tenant' containing value 'tenant-42'", diagnostic);
}
```
<!-- end-snippet -->

Verification failures list recorded requests. Predicate exceptions are reported as mismatches with
the predicate description. Common secret-bearing query values are redacted in diagnostics, but the
raw recorded request still contains the value for exact assertions. Do not print or snapshot raw
requests until application-specific secrets have also been redacted.

For stable request or exchange snapshots, add `XBullet.EasyTesting.Snapshots.Http` and follow the
[snapshot guide](snapshots.md). The canonical implementations are covered by
[`StubHttpMessageHandlerTests`](../../tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs)
and the realistic controller flow by
[`ExternalApiControllerTests`](../../tests/TestApi.IntegrationTests/ExternalApiControllerTests.cs).

Browse the [outbound HTTP API reference](../api/packages/xbullet-easytesting-http.md) and
[snapshot-adapter API reference](../api/packages/xbullet-easytesting-snapshots-http.md).
