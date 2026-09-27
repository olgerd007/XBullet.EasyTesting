# Test logs, traces, metrics, and time

Use `XBullet.EasyTesting.Observability` when logs, distributed activities, metrics, or time-dependent
behavior form part of an integration-test contract. It installs scenario-owned collectors and can
replace `TimeProvider` with deterministic fake time.

## Install and enable capture

```shell
dotnet add package XBullet.EasyTesting.Observability
```

Import `XBullet.EasyTesting.Observability`; signal assertions also use
`System.Diagnostics`, `System.Diagnostics.Metrics`, and `Microsoft.Extensions.Logging`.

Enable all signals with `UseObservability()`. Configure exact activity-source and meter allowlists
to reduce noise, and opt into fake time only when the application resolves `TimeProvider` from DI:

```csharp
using var factory = TestApiHostSettings.CreateBuilder()
    .UseObservability(options => options
        .UseFakeTime(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero))
        .CaptureActivitySource("Orders")
        .CaptureMeter("Orders"))
    .Build();

await using var scope = await factory.CreateTestScenarioScopeAsync();
var observability = scope.GetObservability();
```

When no activity-source or meter names are configured, all sources or meters are captured. Prefer
allowlists in large applications to keep bounded collectors focused and failure output readable.

## Structured logs and deterministic time

The log collector retains category, level, event ID, template, structured state, scopes, formatted
message, exception, and timestamp. Fake time controls capture timestamps and timers created with the
injected `TimeProvider`:

<!-- snippet: tests/TestApi.IntegrationTests/ObservabilityTests.cs#docs-observability-logs-time -->
```csharp
[Fact]
public async Task Captures_structured_logs_scopes_and_fake_time_from_the_scenario_host()
{
    using var factory = TestApiHostSettings.CreateBuilder()
        .UseObservability(options => options.UseFakeTime(StartTime))
        .Build();
    await using var scope = await factory.CreateTestScenarioScopeAsync(
        cancellationToken: TestContext.Current.CancellationToken);
    var observability = scope.GetObservability();
    var logger = scope.Services.GetRequiredService<ILogger<ObservabilityTests>>();

    using (logger.BeginScope(new Dictionary<string, object?>
    {
        ["CorrelationId"] = "scenario-42"
    }))
    {
        logger.LogInformation(
            new EventId(42, "OrderProcessed"),
            "Processed order {OrderId}",
            701);
    }

    var entry = Assert.Single(
        observability.Logs.Entries,
        entry => entry.Category == typeof(ObservabilityTests).FullName);
    Assert.Equal(StartTime, entry.Timestamp);
    Assert.Equal(LogLevel.Information, entry.Level);
    Assert.Equal(42, entry.EventId.Id);
    Assert.Equal("Processed order {OrderId}", entry.MessageTemplate);
    Assert.Equal(701, entry.State["OrderId"]);
    var capturedScope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
        Assert.Single(entry.Scopes));
    Assert.Equal("scenario-42", capturedScope["CorrelationId"]);
    observability.Logs.Should()
        .ContainMessage("Processed order 701", LogLevel.Information);

    var delay = Task.Delay(
        TimeSpan.FromMinutes(5),
        scope.Services.GetRequiredService<TimeProvider>(),
        TestContext.Current.CancellationToken);
    Assert.False(delay.IsCompleted);
    observability.Time!.Advance(TimeSpan.FromMinutes(5));
    await delay;
    Assert.Equal(StartTime.AddMinutes(5), observability.Time.GetUtcNow());
}
```
<!-- end-snippet -->

Advance fake time instead of sleeping. Code that calls `DateTime.UtcNow`, creates a timer without
the injected provider, or talks to an external process is not controlled by this clock.

## Activity relationships and metric measurements

Completed activities retain trace and span identifiers, parent relationships, kind, status,
duration, tags, baggage, and events. Metric measurements retain the instrument name, meter, unit,
value, tags, and timestamp:

<!-- snippet: tests/TestApi.IntegrationTests/ObservabilityTests.cs#docs-observability-traces-metrics -->
```csharp
[Fact]
public async Task Captures_activity_relationships_events_status_and_metrics()
{
    const string sourceName = "EasyTesting.Tests.Orders";
    const string meterName = "EasyTesting.Tests.OrderMetrics";
    using var factory = TestApiHostSettings.CreateBuilder()
        .UseObservability(options => options
            .UseFakeTime(StartTime)
            .CaptureActivitySource(sourceName)
            .CaptureMeter(meterName))
        .Build();
    await using var scope = await factory.CreateTestScenarioScopeAsync(
        cancellationToken: TestContext.Current.CancellationToken);
    var observability = scope.GetObservability();

    using (var source = new ActivitySource(sourceName))
    {
        using var parent = source.StartActivity("orders.process", ActivityKind.Consumer);
        Assert.NotNull(parent);
        parent.SetTag("messaging.destination", "orders");
        parent.AddBaggage("tenant.id", "tenant-42");

        using var child = source.StartActivity("orders.persist", ActivityKind.Internal);
        Assert.NotNull(child);
        child.AddEvent(new ActivityEvent(
            "database.saved",
            tags: new ActivityTagsCollection
            {
                ["entity.count"] = 1
            }));
        child.SetStatus(ActivityStatusCode.Ok);
        child.Stop();
        parent.SetStatus(ActivityStatusCode.Error, "Expected sample status");
        parent.Stop();
    }

    var parentEntry = Assert.Single(
        observability.Activities.Entries,
        entry => entry.OperationName == "orders.process");
    var childEntry = Assert.Single(
        observability.Activities.Entries,
        entry => entry.OperationName == "orders.persist");
    Assert.Equal(parentEntry.TraceId, childEntry.TraceId);
    Assert.Equal(parentEntry.SpanId, childEntry.ParentSpanId);
    Assert.Equal(ActivityStatusCode.Error, parentEntry.Status);
    Assert.Equal("orders", parentEntry.Tags["messaging.destination"]);
    Assert.Equal("tenant-42", parentEntry.Baggage["tenant.id"]);
    var activityEvent = Assert.Single(childEntry.Events);
    Assert.Equal("database.saved", activityEvent.Name);
    Assert.Equal(1, activityEvent.Tags["entity.count"]);
    observability.Activities.Should()
        .ContainActivity("orders.process", ActivityStatusCode.Error, sourceName)
        .ContainActivity("orders.persist", ActivityStatusCode.Ok, sourceName);

    using var meter = new Meter(meterName);
    var counter = meter.CreateCounter<long>("orders.processed", unit: "orders");
    var duration = meter.CreateHistogram<double>("orders.duration", unit: "ms");
    var queueDepth = 7;
    _ = meter.CreateObservableGauge("orders.queue.depth", () => queueDepth);
    counter.Add(3, new KeyValuePair<string, object?>("region", "eu"));
    duration.Record(12.5);
    observability.Metrics.CollectObservableMeasurements();

    observability.Metrics.Should()
        .ContainMeasurement("orders.processed", 3L, meterName)
        .ContainMeasurement("orders.duration", 12.5, meterName)
        .ContainMeasurement("orders.queue.depth", 7, meterName);
    var counterMeasurement = Assert.Single(
        observability.Metrics.Measurements,
        measurement => measurement.InstrumentName == "orders.processed");
    Assert.Equal("orders", counterMeasurement.Unit);
    Assert.Equal("eu", counterMeasurement.Tags["region"]);
    Assert.Equal(StartTime, counterMeasurement.Timestamp);
}
```
<!-- end-snippet -->

Call `CollectObservableMeasurements()` when a gauge or other observable instrument must be sampled
at a known point. Counter and histogram recordings are captured when they occur.

## Fluent assertions

Each collector supports `HaveCount` plus positive and negative assertions:

```csharp
observability.Logs.Should()
    .ContainMessage("Processed order 701", LogLevel.Information)
    .NotContainMessage("Unhandled", LogLevel.Error);
observability.Activities.Should()
    .ContainActivity("orders.process", ActivityStatusCode.Ok, "Orders");
observability.Metrics.Should()
    .ContainMeasurement("orders.processed", 1L, "Orders");
```

Filters are exact for category, source, and meter names. Use raw `Entries` or `Measurements` when a
test must inspect structured state, trace relationships, tags, events, units, or ordering.

## Failure diagnostics and cleanup

When a scenario callback throws, observability is captured into `TestScenarioDiagnostics` before
the collectors and host are disposed:

<!-- snippet: tests/TestApi.IntegrationTests/ObservabilityTests.cs#docs-observability-diagnostics -->
```csharp
[Fact]
public async Task Captured_logs_are_included_in_failure_diagnostics()
{
    using var factory = TestApiHostSettings.CreateBuilder()
        .UseObservability()
        .Build();

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        factory.RunInTestScenarioScopeAsync(
            (scope, _) =>
            {
                using var source = new ActivitySource("Diagnostics.ActivitySource");
                using var activity = source.StartActivity("diagnostic-operation");
                activity?.SetTag("failure.kind", "expected");
                activity?.Stop();

                using var meter = new Meter("Diagnostics.Meter");
                meter.CreateCounter<long>("diagnostic.failures").Add(1);

                scope.Services
                    .GetRequiredService<ILogger<ObservabilityTests>>()
                    .LogError("Diagnostic failure {Code}", 503);
                throw new InvalidOperationException("Expected test failure.");
            },
            cancellationToken: TestContext.Current.CancellationToken));

    var diagnostics = Assert.IsType<TestScenarioDiagnostics>(
        exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);
    var serialized = JsonSerializer.Serialize(diagnostics);
    Assert.Contains("Observability", serialized);
    Assert.Contains("Diagnostic failure 503", serialized);
    Assert.Contains("Code", serialized);
    Assert.Contains("diagnostic-operation", serialized);
    Assert.Contains("diagnostic.failures", serialized);
}
```
<!-- end-snippet -->

Collectors are bounded; once full, they retain the configured number of recent entries. Dispose the
scenario or factory to detach listeners and logger providers. Do not keep a collector across
parallel scenarios: signal listeners are process-wide, while each scenario should own and filter
its capture.

Logs, tags, baggage, events, and metric dimensions can contain sensitive data. Assert only necessary
fields and configure application telemetry to avoid secrets; diagnostics preserve captured values.
The complete success, negative assertion, capacity, and empty-diagnostic behavior is executable in
[`ObservabilityTests`](../../tests/TestApi.IntegrationTests/ObservabilityTests.cs).

Browse the [observability API reference](../api/packages/xbullet-easytesting-observability.md).
