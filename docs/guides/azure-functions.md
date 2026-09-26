# Test .NET isolated Azure Functions

This guide covers in-memory invocation, trigger data, bindings, middleware, and Durable activity dispatch for .NET isolated Azure Functions.

## Azure Functions isolated worker

Reference `XBullet.EasyTesting.AzureFunctions` to resolve function classes from a test service provider and invoke isolated-worker entry points directly. `TestFunctionContext` supplies non-null trace, binding, retry, function-definition, feature, item, service, and cancellation state:

```csharp
var recorder = new RecordingTriggerInvocationSink();
await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<ProcessOrderHttpFunction>()
    .AddFunction<CleanupTimerFunction>()
    .AddFunction<ProcessOrderKafkaFunction>()
    .AddFunction<AdditionalTriggerFunctions>()
    .UseMiddleware(async (context, next) =>
    {
        context.Items["test-middleware"] = "before";
        await next(context);
        context.Items["test-middleware"] = "after";
    })
    .ConfigureServices(services =>
        services.AddSingleton<ITriggerInvocationSink>(recorder))
    .Build();
```

Build an in-memory HTTP request and inspect the returned response:

```csharp
var request = host.HttpRequest(nameof(ProcessOrderHttpFunction))
    .WithMethod(HttpMethod.Post)
    .WithUrl("/api/orders")
    .WithJsonBody(new CreateOrderRequest("order-42", 3))
    .Build();

var function = host.GetRequiredService<ProcessOrderHttpFunction>();
var response = await function.RunAsync(request, request.FunctionContext);
var body = await response.ReadBodyAsJsonAsync<AcceptedOrderResponse>();
```

HTTP requests are automatically captured as an `httpTrigger` input. Retry, tracing, custom binding data, items, and typed invocation features can be configured fluently:

```csharp
var context = host.CreateContext("ProcessOrder")
    .WithRetry(retryCount: 2, maxRetryCount: 5)
    .WithTrace(traceParent, traceState)
    .WithBindingData("tenant", "test-tenant")
    .WithFeature(new TestFeature());
```

Timer and Kafka values retain their simple direct builders and also provide capturable trigger forms through `BuildTrigger`, `JsonTrigger`, and `JsonBatchTrigger`:

```csharp
var timer = AzureFunctionTestHost.Timer()
    .PastDue()
    .WithSchedule(last, next)
    .Build();

var kafkaMessage = KafkaTriggerData.Json(
    new KafkaOrderMessage("order-42", 3));
```

Kafka-triggered functions can be composed with outbound HTTP stubs. The sample
`PriceOrderKafkaFunction` consumes an order-pricing event, fetches the product price from an
external API, and records the calculated total:

```csharp
var pricingApi = new StubHttpMessageHandler();
pricingApi
    .When(HttpMethod.Get, "/products/42/price")
    .RespondJson(new { ProductId = 42, UnitPrice = 19.95m, Currency = "USD" });

await using var host = AzureFunctionTestHost.CreateBuilder()
    .AddFunction<PriceOrderKafkaFunction>()
    .ConfigureServices(services =>
    {
        services.AddSingleton<ITriggerInvocationSink>(recorder);
        services
            .AddHttpClient<IOrderPricingClient, OrderPricingClient>(client =>
                client.BaseAddress = new Uri("https://pricing.example.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => pricingApi);
    })
    .Build();

var trigger = KafkaTriggerData.JsonTrigger(
    new OrderPricingRequestedMessage("order-42", ProductId: 42, Quantity: 3),
    topic: "order-pricing");

await host.InvokeAsync<PriceOrderKafkaFunction, string>(
    nameof(PriceOrderKafkaFunction),
    trigger,
    (function, message, context) => function.RunAsync(message, context));

pricingApi.VerifyCalled(HttpMethod.Get, "/products/42/price");
```

The function lets HTTP failures propagate so the deployed Kafka trigger can retry the event;
the integration test also verifies that a failed pricing call produces no recorded result.

Service Bus, Queue Storage, Blob, Event Grid, and Event Hubs builders return `TestTriggerData<T>`. Passing it to `InvokeAsync` captures the input and binding metadata, runs configured worker middleware in order, and invokes the function:

```csharp
var trigger = AzureFunctionTestHost.ServiceBusTrigger()
    .WithJsonBody(new OrderMessage("order-42", 3))
    .WithMessageId("message-1")
    .WithCorrelationId("correlation-1")
    .Build();

var invocation = await host.InvokeAsync<OrderFunction, string>(
    "ProcessOrderServiceBus",
    trigger,
    (function, message, context) => function.RunAsync(message, context));

Assert.Equal("message-1", invocation.Context.BindingContext.BindingData["MessageId"]);
Assert.Equal(trigger.Value, invocation.Context.Bindings.GetInput<string>("message"));
```

Equivalent entry points are `QueueTrigger()`, `BlobTrigger()`, `EventGridTrigger()`, and `EventHubsTrigger()`. Builders expose trigger-specific payload and metadata methods, including Event Hubs batches and Blob streams.

For functions returning a multiple-output POCO, the invocation captures every public result property and recognizes worker output attributes such as `QueueOutput` and `BlobOutput`:

```csharp
var invocation = await host.InvokeAsync<RouteOrderFunction, RouteOrderOutput>(
    context,
    (function, testContext) => function.RunAsync(trigger.Value, testContext));

invocation.Context.Bindings.Should()
    .HaveCount(2)
    .HaveValue("QueueMessage", expectedQueueMessage)
    .HaveValue("BlobDocument", expectedBlobDocument)
    .NotContain("UnexpectedOutput");
```

These tests exercise function code, dependency injection, serialization, binding metadata, middleware ordering, retry-aware behavior, and output behavior without Azure Functions Core Tools or live Azure services. They do not validate host indexing, binding expressions, broker connectivity, checkpoints, or deployment configuration; keep a smaller runtime-level test suite for those concerns.
