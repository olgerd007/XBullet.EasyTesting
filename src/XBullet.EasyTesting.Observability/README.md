# XBullet.EasyTesting.Observability

Structured logs, distributed traces, metrics, and deterministic time for XBullet integration-test scenarios.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Observability
```

## Example

```csharp
using var factory = EasyTestHost.Create<Program>()
    .UseObservability(options => options
        .UseFakeTime(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero))
        .CaptureActivitySource("Orders")
        .CaptureMeter("Orders"))
    .Build();

await using var scope = await factory.CreateTestScenarioScopeAsync();
var observability = scope.GetObservability();

observability.Time!.Advance(TimeSpan.FromMinutes(5));
observability.Logs.Should()
    .ContainMessage("Order processed", LogLevel.Information);
observability.Activities.Should()
    .ContainActivity("ProcessOrder", ActivityStatusCode.Ok, "Orders");
observability.Metrics.Should()
    .ContainMeasurement("orders.processed", 1L, "Orders");
```

Log entries retain their category, level, event ID, message template, structured state, scopes,
formatted message, exception, and capture timestamp. Completed activities retain trace and span
identifiers, status, tags, baggage, and events. Metric measurements retain instrument metadata,
values, tags, and capture timestamps; call `CollectObservableMeasurements()` when an observable
instrument should be sampled explicitly.

All three collectors are bounded and included in scenario failure diagnostics before the host and
observability resource are disposed. Activity-source and meter filters are exact-name allowlists;
when no names are configured, all sources and meters are captured.
