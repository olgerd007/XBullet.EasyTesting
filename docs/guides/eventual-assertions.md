# Wait for background work

Use `Eventually.AssertAsync` to retry an assertion until it passes, or `Eventually.WaitUntilAsync`
to poll a condition until it returns `true`. These test-framework-independent helpers live in
`XBullet.EasyTesting`, target .NET 8, .NET 9, and .NET 10, and work with messages, database state,
logs, and other asynchronously produced results.

```shell
dotnet add package XBullet.EasyTesting
```

Import `XBullet.EasyTesting`. Pass the test framework's cancellation token whenever one is available.

## Choose an assertion or condition

An assertion callback succeeds when it completes without throwing. The default assertion policy
retries every exception except `OperationCanceledException`. Keep the callback focused on
read-only assertions; setup, commands, and other side effects belong outside the retry loop.

A condition callback succeeds when it returns `true`. A `false` result is retried; any exception
propagates immediately. Use this form for asynchronous database queries so query errors remain
visible immediately.

Both helpers accept synchronous callbacks, parameterless asynchronous callbacks, and asynchronous
callbacks receiving a cancellation token. The token-aware form receives a token combining caller
cancellation and the polling deadline; forward it to database, HTTP, and other asynchronous APIs.

## Wait for a published message

This example starts a simulated background publisher and waits for its recorded payload. A retry
filter restricts retries to messaging verification failures. The test awaits its own background
task before the scope is disposed, including on assertion failure.

<!-- snippet: tests/TestApi.IntegrationTests/EventuallyExamples.cs#docs-eventually-messages -->
```csharp
[Fact]
public async Task Waits_for_a_background_publisher()
{
    using var factory = new TestApiFactory();
    await factory.RunInTestScenarioScopeAsync(async (_, cancellationToken) =>
    {
        // Stand in for work queued to the application's background publisher.
        var publication = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            factory.PublishedMessages.Record(
                MessageTransportNames.Kafka, "orders.created", new { OrderId = 42 });
        }, cancellationToken);

        try
        {
            await Eventually.AssertAsync(
                () => factory.PublishedMessages.Should()
                    .ContainSingle(MessageTransportNames.Kafka, "orders.created")
                    .HavePayload(new { OrderId = 42 }),
                new EventuallyOptions
                {
                    Description = "Order 42 is published",
                    ShouldRetry = error => error is RecordedMessageVerificationException
                },
                cancellationToken);
        }
        finally
        {
            await publication;
        }
    }, cancellationToken: TestContext.Current.CancellationToken);
}
```
<!-- end-snippet -->

## Wait for persisted state

Query through a fresh `DbContext` on each attempt. Reusing a tracked entity can hide a background
update, and sharing one context between the worker and poller is unsafe. This example uses the
in-memory EF test factory; the same polling helper supports relational providers.

<!-- snippet: tests/TestApi.IntegrationTests/EventuallyExamples.cs#docs-eventually-database -->
```csharp
[Fact]
public async Task Waits_for_a_background_database_write()
{
    using var factory = new InMemoryTestApiFactory();
    await factory.RunInTestScenarioScopeAsync(async (scope, cancellationToken) =>
    {
        var write = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            await factory.Database(scope)
                .Seed(new Product { Id = 42, Name = "Processed", Price = 10m })
                .ExecuteAsync(cancellationToken);
        }, cancellationToken);

        try
        {
            // Each query opens a fresh DbContext, avoiding stale tracked entities.
            await Eventually.WaitUntilAsync(
                token => factory.QueryDatabaseAsync(
                    scope,
                    (database, queryToken) => database.Products.AsNoTracking()
                        .AnyAsync(product => product.Id == 42 && product.Name == "Processed", queryToken),
                    token),
                new EventuallyOptions { Description = "Product 42 is processed" },
                cancellationToken);
        }
        finally
        {
            await write;
        }
    }, cancellationToken: TestContext.Current.CancellationToken);
}
```
<!-- end-snippet -->

## Configure polling

The first attempt runs immediately. Later attempts run after the previous callback has completed
and the polling delay has elapsed. Attempts never overlap. Options are immutable after construction
and can be shared between independent waits.

| Option | Default | Behavior |
| --- | --- | --- |
| `Timeout` | Five seconds | Total cooperative deadline, including callbacks and delays |
| `PollInterval` | Fifty milliseconds | Delay between completed attempts, limited to remaining time |
| `TimeProvider` | `TimeProvider.System` | Clock for elapsed time, deadline cancellation, and delays |
| `Description` | `null` | Expected behavior included in timeout diagnostics |
| `ShouldRetry` | `null` | Assertion exception filter; `null` retries every non-cancellation failure |

Timeout and interval must be positive and no greater than 4,294,967,294 milliseconds, the timer
limit. The time provider must be non-null. Custom-options overloads take both the options and
cancellation token; pass `CancellationToken.None` when no caller token exists.

Use `ShouldRetry` when assertion callbacks can also throw programming or infrastructure errors.
The filter is never called for cancellation exceptions and has no effect on condition callbacks.

For deterministic tests, pass a fake `TimeProvider` and advance it from the test driver. Polling
does not advance fake time itself. When using [observability fake time](observability.md), the
host's provider can also be passed to `EventuallyOptions.TimeProvider`.

## Diagnose failures and cancellation

An expired deadline throws `EventuallyTimeoutException`, derived from `TimeoutException`. It
reports `AttemptCount`, `Elapsed`, and `Timeout`, includes the description in its message, and
retains the last retried assertion exception as `InnerException`. A condition timeout has no inner
exception. A result arriving at or after the deadline is rejected.

Caller cancellation throws `OperationCanceledException` with the original caller token. An
independent cancellation exception from a callback propagates without retry. Caller cancellation
takes precedence when it coincides with the deadline.

Within `RunInTestScenarioScopeAsync` or `ScopedTest.RunAsync`, a timeout receives the usual scenario
diagnostics before cleanup under `TestScenarioDiagnostics.ExceptionDataKey`. The inner assertion
failure remains available. Diagnostic messages may contain actual assertion values; use synthetic
test data and safe descriptions.

## Lifecycle and limits

Timeouts are cooperative. The deadline cancels the token supplied to token-aware callbacks, and
the helper awaits the callback's completion and cleanup. Synchronous callbacks, parameterless
asynchronous callbacks, or code ignoring cancellation can exceed the configured timeout. A wait
never abandons a running callback or starts another attempt alongside it.

Keep the scenario alive for the entire wait and finish any separately started background tasks
before disposing it. A successful poll proves that the expectation held for one observation; it
does not prove that no later message, duplicate, or state change will occur. For negative behavior,
wait for a known completion signal before asserting absence.

## Related documentation

- [Messaging](messaging.md)
- [Entity Framework Core](entity-framework-core.md)
- [Observability](observability.md)
- [Scenarios and isolation](../concepts/scenarios-and-isolation.md)
- [Core API reference](../api/packages/xbullet-easytesting.md)
- [Executable examples](../../tests/TestApi.IntegrationTests/EventuallyExamples.cs)
- [Timeout and cancellation tests](../../tests/TestApi.IntegrationTests/EventuallyTests.cs)
