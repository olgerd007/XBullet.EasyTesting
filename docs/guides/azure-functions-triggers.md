# Azure Functions trigger recipes

This page shows how to construct trigger values and metadata for .NET isolated function tests.
Start with the [Azure Functions test-host guide](azure-functions.md) for installation, DI,
middleware, invocation scopes, and limitations.

Each capturable builder returns `TestTriggerData<T>`. Pass it to `InvokeAsync` so the test host can
capture the input, populate `BindingContext.BindingData`, describe the input binding in
`FunctionDefinition`, run middleware, and invoke the function.

## HTTP trigger

HTTP requests can be invoked directly and are captured as the `httpTrigger` input:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-http-trigger -->
```csharp
[Fact]
public async Task Http_trigger_accepts_an_order_and_records_the_invocation()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var function = host.GetRequiredService<ProcessOrderHttpFunction>();
    var request = host.HttpRequest(
            nameof(ProcessOrderHttpFunction),
            TestContext.Current.CancellationToken)
        .WithMethod(HttpMethod.Post)
        .WithUrl("/api/orders?source=integration-test")
        .WithHeader("x-correlation-id", "test-correlation")
        .WithJsonBody(new CreateOrderRequest("order-42", 3))
        .Build();

    var response = await function.RunAsync(request, request.FunctionContext);
    var payload = await response.ReadBodyAsJsonAsync<AcceptedOrderResponse>(
        cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.Same(request, request.FunctionContext is TestFunctionContext testContext
        ? testContext.Bindings.Inputs["request"]
        : null);
    Assert.Equal(new AcceptedOrderResponse("order-42", 3, "accepted"), payload);
    Assert.Equal(
        new TriggerInvocation("http", "order-42", 3),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

Configure method, URL, headers, raw or JSON body, and cancellation. Response helpers read text or
JSON without replacing the function's real response object.

## Timer trigger

Use `Timer()` to describe schedule history and whether the invocation is past due:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-timer-trigger -->
```csharp
[Fact]
public async Task Timer_trigger_exposes_schedule_and_past_due_state()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = AzureFunctionTestHost.Timer()
        .PastDue()
        .WithSchedule(
            new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 14, 9, 5, 0, DateTimeKind.Utc))
        .BuildTrigger();
    var timer = trigger.Value;

    var invocation = await host.InvokeAsync<CleanupTimerFunction, TimerInfo>(
        nameof(CleanupTimerFunction),
        trigger,
        (function, timerInfo, context) => function.RunAsync(timerInfo, context),
        TestContext.Current.CancellationToken);

    Assert.True(timer.IsPastDue);
    Assert.NotNull(timer.ScheduleStatus);
    Assert.Same(timer, invocation.Context.Bindings.GetInput<TimerInfo>("timer"));
    Assert.Equal("timerTrigger", invocation.Context.FunctionDefinition.InputBindings["timer"].Type);
    Assert.Equal(
        new TriggerInvocation("timer", "cleanup", 0, IsPastDue: true),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

This supplies `TimerInfo`; it does not evaluate a CRON expression or advance a scheduler.

## Kafka trigger

`KafkaTriggerData.JsonTrigger` serializes one value and adds topic and partition metadata:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-kafka-trigger -->
```csharp
[Fact]
public async Task Kafka_trigger_deserializes_and_records_an_order()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = KafkaTriggerData.JsonTrigger(
        new KafkaOrderMessage("order-kafka", 7),
        topic: "orders",
        partitionKey: "order-kafka");

    var invocation = await host.InvokeAsync<ProcessOrderKafkaFunction, string>(
        nameof(ProcessOrderKafkaFunction),
        trigger,
        (function, message, context) => function.RunAsync(message, context),
        TestContext.Current.CancellationToken);

    Assert.Equal("orders", invocation.Context.BindingContext.BindingData["Topic"]);
    Assert.Equal("order-kafka", invocation.Context.BindingContext.BindingData["PartitionKey"]);
    Assert.Equal(
        new TriggerInvocation("kafka", "order-kafka", 7),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

A realistic function can combine the trigger with other test boundaries. Here the real function
deserializes a Kafka payload, calls its typed pricing client through an HTTP stub, and records an
enriched result:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-kafka-realistic -->
```csharp
[Fact]
public async Task Kafka_pricing_function_calls_external_api_and_records_the_enriched_order()
{
    var recorder = new RecordingTriggerInvocationSink();
    var pricingApi = new StubHttpMessageHandler();
    pricingApi
        .When(HttpMethod.Get, "/products/42/price")
        .RespondJson(new { ProductId = 42, UnitPrice = 19.95m, Currency = "USD" });
    await using var host = CreatePricingHost(recorder, pricingApi);
    var trigger = KafkaTriggerData.JsonTrigger(
        new OrderPricingRequestedMessage("order-priced", 42, 3),
        topic: "order-pricing",
        partitionKey: "order-priced");

    var invocation = await host.InvokeAsync<PriceOrderKafkaFunction, string>(
        nameof(PriceOrderKafkaFunction),
        trigger,
        (function, message, context) => function.RunAsync(message, context),
        TestContext.Current.CancellationToken);

    Assert.True(invocation.FunctionExecuted);
    Assert.Equal("order-pricing", invocation.Context.BindingContext.BindingData["Topic"]);
    pricingApi.VerifyCalled(HttpMethod.Get, "/products/42/price");
    Assert.Equal(
        new TriggerInvocation(
            "kafka-pricing",
            "order-priced",
            3,
            Detail: "USD",
            Amount: 59.85m),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

Let downstream failures escape when deployed trigger retry policy should observe them. The
canonical suite verifies that a failed pricing request records no enriched result.

## Azure Service Bus trigger

Configure JSON or binary content and broker metadata such as message ID, correlation ID, subject,
session ID, delivery count, and enqueue time:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-servicebus-trigger -->
```csharp
[Fact]
public async Task Service_Bus_trigger_captures_body_and_broker_metadata()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = AzureFunctionTestHost.ServiceBusTrigger()
        .WithJsonBody(new KafkaOrderMessage("order-service-bus", 4))
        .WithMessageId("message-1")
        .WithCorrelationId("correlation-1")
        .Build();

    var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, string>(
        "ProcessOrderServiceBus",
        trigger,
        (function, message, context) => function.RunServiceBusAsync(message, context),
        TestContext.Current.CancellationToken);

    Assert.True(invocation.FunctionExecuted);
    Assert.Equal(trigger.Value, invocation.Context.Bindings.Inputs["message"]);
    Assert.Equal("message-1", invocation.Context.BindingContext.BindingData["MessageId"]);
    Assert.Equal("correlation-1", invocation.Context.BindingContext.BindingData["CorrelationId"]);
    Assert.Equal("serviceBusTrigger", invocation.Context.FunctionDefinition.InputBindings["message"].Type);
    Assert.EndsWith(
        ".AdditionalTriggerFunctions.RunServiceBusAsync",
        invocation.Context.FunctionDefinition.EntryPoint,
        StringComparison.Ordinal);
    Assert.EndsWith(
        "TestFunctions.dll",
        invocation.Context.FunctionDefinition.PathToAssembly,
        StringComparison.OrdinalIgnoreCase);
    Assert.Equal(2, invocation.Context.FunctionDefinition.Parameters.Length);
    Assert.Equal(
        new TriggerInvocation("service-bus", "order-service-bus", 4),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

## Queue Storage trigger

The queue builder captures message content plus queue metadata such as message ID, insertion and
expiration time, pop receipt, next-visible time, and dequeue count:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-queue-trigger -->
```csharp
[Fact]
public async Task Queue_trigger_captures_dequeue_metadata()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = AzureFunctionTestHost.QueueTrigger()
        .WithJsonBody(new KafkaOrderMessage("order-queue", 5))
        .WithMessageId("queue-message-1")
        .WithDequeueCount(3)
        .Build();

    var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, string>(
        "ProcessOrderQueue",
        trigger,
        (function, message, context) => function.RunQueueAsync(message, context),
        TestContext.Current.CancellationToken);

    Assert.Equal(3, invocation.Context.BindingContext.BindingData["DequeueCount"]);
    Assert.Equal(
        new TriggerInvocation("queue", "order-queue", 5),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

## Blob trigger

Supply text, bytes, a stream, or JSON content and the blob path metadata:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-blob-trigger -->
```csharp
[Fact]
public async Task Blob_trigger_provides_content_stream_and_path_metadata()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = AzureFunctionTestHost.BlobTrigger()
        .WithJsonContent(new KafkaOrderMessage("order-blob", 6))
        .WithPath("orders/order-blob.json")
        .Build();
    await using var blob = trigger.Value;

    var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, Stream>(
        "ProcessOrderBlob",
        trigger,
        (function, stream, context) => function.RunBlobAsync(stream, context),
        TestContext.Current.CancellationToken);

    Assert.Equal("orders/order-blob.json", invocation.Context.BindingContext.BindingData["BlobTrigger"]);
    Assert.Equal(
        new TriggerInvocation("blob", "order-blob", 6),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

Dispose streams owned by the test after invocation, as the example does with `await using`.

## Event Grid trigger

Build a complete Event Grid envelope with ID, event type, subject, event time, data version, and
serialized data:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-eventgrid-trigger -->
```csharp
[Fact]
public async Task Event_Grid_trigger_provides_envelope_and_event_metadata()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = AzureFunctionTestHost.EventGridTrigger()
        .WithId("event-1")
        .WithEventType("order.created")
        .WithSubject("/orders/order-event-grid")
        .WithData(new KafkaOrderMessage("order-event-grid", 8))
        .Build();

    var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, string>(
        "ProcessOrderEventGrid",
        trigger,
        (function, eventJson, context) => function.RunEventGridAsync(eventJson, context),
        TestContext.Current.CancellationToken);

    Assert.Equal("order.created", invocation.Context.BindingContext.BindingData["EventType"]);
    Assert.Equal(
        new TriggerInvocation("event-grid", "order-event-grid", 8),
        Assert.Single(recorder.Invocations));
}
```
<!-- end-snippet -->

## Event Hubs trigger

Add one or more events to exercise a batch-triggered function and set partition and consumer
metadata:

<!-- snippet: tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs#docs-functions-eventhubs-trigger -->
```csharp
[Fact]
public async Task Event_Hubs_trigger_supports_batches()
{
    var recorder = new RecordingTriggerInvocationSink();
    await using var host = CreateHost(recorder);
    var trigger = AzureFunctionTestHost.EventHubsTrigger()
        .AddJsonEvent(new KafkaOrderMessage("order-event-hubs-1", 9))
        .AddJsonEvent(new KafkaOrderMessage("order-event-hubs-2", 10))
        .WithPartitionId("2")
        .Build();

    var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, string[]>(
        "ProcessOrderEventHubs",
        trigger,
        (function, events, context) => function.RunEventHubsAsync(events, context),
        TestContext.Current.CancellationToken);

    Assert.Equal("2", invocation.Context.BindingContext.BindingData["PartitionId"]);
    Assert.Equal(2, recorder.Invocations.Count);
    Assert.Contains(
        new TriggerInvocation("event-hubs", "order-event-hubs-1", 9),
        recorder.Invocations);
    Assert.Contains(
        new TriggerInvocation("event-hubs", "order-event-hubs-2", 10),
        recorder.Invocations);
}
```
<!-- end-snippet -->

## Choosing the next testing layer

These builders model values and metadata delivered to function code. They do not connect to Kafka
or Azure services, validate binding expressions, create checkpoints, enforce settlement rules, or
prove that the deployed host indexed the function. Use
[containerized infrastructure](testcontainers.md) or an isolated deployed environment when those
runtime behaviors are the subject of the test.

All trigger recipes execute in
[`FunctionTriggerTests`](../../tests/TestFunctions.IntegrationTests/FunctionTriggerTests.cs).

See the [Azure Functions API reference](../api/packages/xbullet-easytesting-azurefunctions.md) for
the complete trigger-builder surface.
