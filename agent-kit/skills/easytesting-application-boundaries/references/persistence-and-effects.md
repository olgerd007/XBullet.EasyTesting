# Persistence and external effects

## Database actions

Reuse a derived `EntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` for relational
tests, or `InMemoryEntityFrameworkWebApplicationFactory<TEntryPoint, TDbContext>` for behavior
that does not rely on relational constraints. Import `XBullet.EasyTesting.EntityFrameworkCore`.
The selected provider remains a dependency of the test project.

Use `factory.Database(scope).Seed(...).ExecuteAsync(cancellationToken)` for setup. Use
`QueryDatabaseAsync(scope, (database, token) => ..., cancellationToken)` for durable assertions
through a correctly scoped context; pass the delegate's token to EF operations and use
`AsNoTracking()` for read-only queries. Database builders execute once; arranging a builder without
calling `ExecuteAsync` does not seed data.

Choose independent per-scenario databases or a deliberate shared-database cleanup strategy. Check
cleanup and transaction support against the provider. Do not share one `DbContext` between test
threads or assume the factory gate isolates independent factories.

## Outbound HTTP

Import `XBullet.EasyTesting.Http`. Replace the application's existing named or typed client's
handler in the test host; do not bypass production HttpClient configuration accidentally.

```csharp
handler.When(HttpMethod.Post, "/orders")
    .WithRequestHeader("X-Tenant", "tenant-42")
    .WithJsonRequestBody(new { OrderId = 42 })
    .Respond(HttpStatusCode.Accepted);

// Send the request through the application before checking its external effect.
handler.VerifyCalled(HttpMethod.Post, "/orders");
```

Here `handler` is the scenario's `StubHttpMessageHandler`; imports also include `System.Net`.
Configure the response before the application request and check relevant count, query, payload,
and headers. Arrange sequences and failures only when exercising a defined retry/failure contract.
Use synthetic secrets. Recorded bodies can be disabled or truncated: check capture options before
using a body-dependent matcher or treating a snapshot as complete.

## Messages and telemetry

Import `XBullet.EasyTesting.Messaging`. The application's publisher adapter calls
`messages.RecordAsync(transport, destination, payload, headers, cancellationToken)`; the test
must not record the expected message itself and then present that as application coverage.
Use `messages.Should().ContainSingle(transport, destination).HavePayload(expected)` for one
publication, adding counts, headers, or exact sequence assertions when meaningful. Register a
shared message recorder as a scenario resource.

Import `XBullet.EasyTesting.Observability` for `UseObservability` host configuration and
`scope.GetObservability()`. Capture only relevant activity sources and meters. Advance configured
fake time through the scenario's observability time provider. Fake time affects application code
that uses the injected provider; it cannot make external service clocks deterministic.

## Background completion

`Eventually.AssertAsync` retries assertions; by default it retries exceptions other than
`OperationCanceledException`. Restrict retry exceptions when appropriate so configuration bugs
do not become opaque timeouts. `Eventually.WaitUntilAsync` retries `false` conditions and propagates
exceptions. Configure a bounded deadline and pass cancellation through downstream operations.

Query through a fresh database context per attempt. Await owned background work before scope
cleanup. An absence assertion can pass before a producer starts, so wait for known producer
completion before asserting that a message or outbound call was never emitted.

- [Database workflows](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/entity-framework-core.md)
- [Outbound HTTP](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/outbound-http.md)
- [Published messages](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/messaging.md)
- [Observability](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/observability.md)
- [Eventual assertions](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/eventual-assertions.md)

These upstream links track `main`; prefer release-matched documentation for older packages.
