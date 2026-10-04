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

## Limitations

Only `CallActivityAsync` is emulated; Durable runtime operations such as timers, external events,
sub-orchestrators, and replay state throw `NotSupportedException`.

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting-azurefunctions.html)
- [Detailed Azure Functions guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions.md)
- [Trigger recipes](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions-triggers.md)
- [Durable activity-dispatch boundary](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions-durable.md)
- [Executable Functions examples](https://github.com/olgerd007/XBullet.EasyTesting/tree/main/tests/TestFunctions.IntegrationTests)
