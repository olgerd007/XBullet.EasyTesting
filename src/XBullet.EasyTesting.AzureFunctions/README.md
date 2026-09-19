# XBullet.EasyTesting.AzureFunctions

In-memory test infrastructure for .NET isolated Azure Functions, including function contexts, middleware, bindings, retry state, and fluent trigger data.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.AzureFunctions
```

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

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for invocation and binding examples.
