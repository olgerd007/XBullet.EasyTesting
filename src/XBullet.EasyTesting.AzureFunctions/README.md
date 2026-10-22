# XBullet.EasyTesting.AzureFunctions

In-memory test infrastructure for .NET isolated Azure Functions, including function contexts,
middleware, bindings, retry state, Durable orchestration activity dispatch, and fluent trigger
data.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.AzureFunctions
```

## Example

```csharp
await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<ProcessOrderHttpFunction>()
    .Build();

var request = host.HttpRequest(nameof(ProcessOrderHttpFunction))
    .WithMethod(HttpMethod.Post)
    .WithUrl("/api/orders")
    .WithJsonBody(new CreateOrderRequest("order-42", 3))
    .Build();

var function = host.GetRequiredService<ProcessOrderHttpFunction>();
var response = await function.RunAsync(request, request.FunctionContext);
```

Builders are available for HTTP, timer, Kafka, Service Bus, Queue Storage, Blob, Event Grid, and
Event Hubs triggers.

To reuse a Functions application's `Program.cs`, derive from
`AzureFunctionTestFactory<TEntryPoint>`, override `ConfigureHost`, `ConfigureTestHost`, or
`ConfigureTestServices` as needed, and await `CreateHostAsync(cancellationToken)`. The factory
captures the production generic host at construction, retains its configuration and service
registrations, and exposes the existing invocation and scenario APIs without starting hosted
services or worker transport. It owns the returned host and must be asynchronously disposed.
Worker-native middleware requires explicit registration through `ConfigureTestHost` for in-process
invocation. Startup code before host construction still executes; see the
[factory guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions.md#reuse-programcs-with-a-factory)
for lifetime, cancellation, and timeout behavior.

Use `UseScenarioResource(name, resource)` to register borrowed state implementing the shared
`ITestScenarioResource` contract, then `RunInTestScenarioScopeAsync` to reset it before and after
setup, multiple invocations, and assertions. Scenarios on one host are serialized, and every
invocation still owns a fresh DI scope. `FunctionScenario` and `FunctionScopedTest` provide guarded
domain setup and a test runner without an ASP.NET factory. The package references
`XBullet.EasyTesting` for shared scenario resource, cleanup, and diagnostic contracts.

Invocation and scenario failures attach `TestScenarioDiagnostics` before cleanup. Original
exceptions and cancellation tokens are preserved when cleanup fails; aggregated cleanup errors
are attached in `exception.Data`. Registered resources remain caller-owned. Host singleton services
persist between scenarios unless they participate in the registered reset lifecycle.

Durable orchestrators can use `TestOrchestrationContext` to record activity scheduling and dispatch
each call to a real activity instance:

```csharp
var activity = new CreateOrderActivity(dependency);
var context = new TestOrchestrationContext(async call =>
    call.ActivityName switch
    {
        nameof(CreateOrderActivity) =>
            await activity.RunAsync((CreateOrderRequest)call.Input!),
        _ => throw new InvalidOperationException($"Unexpected activity '{call.ActivityName}'."),
    });

var result = await orchestrator.RunAsync(context);

Assert.Equal(nameof(CreateOrderActivity), context.ActivityCalls.Single().ActivityName);
```

## Orchestration controls

Use `WithInput(request)` and `WithInstanceId("parent-42")` to configure orchestration input and identity.
`GetInput<T>()` returns the retained typed value without serialization.

Timers use manual time starting at `DateTime.UnixEpoch`. Drive `CurrentUtcDateTime` and due timers
with `AdvanceTimeTo(utcDateTime)` or `AdvanceTimeBy(duration)`. Raise events with
`RaiseEvent(name, payload)`; events are buffered and delivered once in FIFO order, matching names
case-insensitively. Timer and event waits support cancellation, and event timeout overloads use simulated time.

Configure `SubOrchestratorHandler` to dispatch child calls and inspect `SubOrchestrationCalls` afterward.
The handler can invoke a real child orchestrator with its own test context. `SendEvent` records
`SentEvents` and delivers events locally when targeting the current instance ID.

`WithReplayState(true)` controls `IsReplaying` for replay branches and log suppression. Logging is
silent by default; supply a caller-owned logger factory with `WithLoggerFactory(factory)` before
creating replay-safe loggers to inspect output.

## Default stubs and fluent overrides

`new TestOrchestrationContext()` returns typed default values for activity and child calls. Override
dispatch with `WithActivityHandler(handler)` and `WithSubOrchestratorHandler(handler)`.
Use `WithActivityHandler("ActivityName", (name, input) => ...)` to route a specific activity.
Names match case-sensitively; named registrations take precedence over the fallback, and registering
the same name again replaces its handler for future calls. All calls are still recorded and type-checked.

Metadata defaults to `TestOrchestrator`, no parent, an empty version, and empty read-only properties.
Override it with `WithName`, `WithParent`, `WithVersion`, and `WithProperties`.

Entity calls return typed defaults, signals complete, and local lock scopes update critical-section
markers. Use `WithEntities(feature)` to provide custom behavior. `NewGuid()` generates a repeatable
per-instance sequence, overridable with `WithGuidGenerator`. Version comparison is ordinal by default;
`WithVersionComparer` can supply another ordering.

`SetCustomStatus` retains `CustomStatus`. `ContinueAsNew` records copied options in
`ContinueAsNewRequests` without restarting or changing current input, time, version, or events.
Use `WithCustomStatus`, `WithCustomStatusHandler`, and `WithContinueAsNewHandler` to configure or observe these stubs.

## Limitations

These controls do not implement durable history, automatic replay, persistence, retries, child-instance
lifecycle management, or remote event delivery. Entity defaults do not implement state or distributed
locking; GUID sequences do not reproduce the runtime algorithm, and continuation requests do not restart execution.

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting-azurefunctions.html)
- [Detailed Azure Functions guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions.md)
- [Trigger recipes](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions-triggers.md)
- [Durable activity-dispatch boundary](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions-durable.md)
- [Complex orchestration recipes](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions-durable-recipes.md)
- [Executable Functions examples](https://github.com/olgerd007/XBullet.EasyTesting/tree/main/tests/TestFunctions.IntegrationTests)
