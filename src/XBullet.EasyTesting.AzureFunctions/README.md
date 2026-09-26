# XBullet.EasyTesting.AzureFunctions

In-memory test infrastructure for .NET isolated Azure Functions, including function contexts, middleware, bindings, retry state, Durable orchestration activity dispatch, and fluent trigger data.

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

Builders are available for HTTP, timer, Kafka, Service Bus, Queue Storage, Blob, Event Grid, and Event Hubs triggers.

Durable orchestrators can use `TestOrchestrationContext` to record activity scheduling and dispatch each call to a real activity instance:

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

- [Detailed Azure Functions guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/azure-functions.md)
- [Executable Functions examples](https://github.com/olgerd007/XBullet.EasyTesting/tree/main/tests/TestFunctions.IntegrationTests)
