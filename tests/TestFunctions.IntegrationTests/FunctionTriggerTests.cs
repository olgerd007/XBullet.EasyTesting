using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using TestFunctions.Functions;
using TestFunctions.External;
using TestFunctions.Models;
using TestFunctions.Services;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Http;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class FunctionTriggerTests
{
    [Fact]
    public async Task Context_exposes_complete_worker_state()
    {
        await using var host = CreateHost(new RecordingTriggerInvocationSink());
        using var cancellation = new CancellationTokenSource();
        var marker = new InvocationMarker("feature-value");
        var context = host.CreateContext("CompleteContext", cancellation.Token)
            .WithTrace("00-test-trace-parent", "vendor=test")
            .WithRetry(2, 5)
            .WithBindingData("custom", "binding-value")
            .WithFeature(marker)
            .WithItem("item", "item-value");

        Assert.Equal("00-test-trace-parent", context.TraceContext.TraceParent);
        Assert.Equal("vendor=test", context.TraceContext.TraceState);
        Assert.Equal("binding-value", context.BindingContext.BindingData["custom"]);
        Assert.Equal(2, context.RetryContext.RetryCount);
        Assert.Equal(5, context.RetryContext.MaxRetryCount);
        Assert.Equal("CompleteContext", context.FunctionDefinition.Name);
        Assert.Equal("CompleteContext", context.FunctionDefinition.EntryPoint);
        Assert.Same(marker, context.Features.Get<InvocationMarker>());
        Assert.Equal("item-value", context.Items["item"]);
        Assert.Equal(cancellation.Token, context.CancellationToken);
    }

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

    [Fact]
    public async Task Kafka_pricing_function_propagates_external_failure_for_retry()
    {
        var recorder = new RecordingTriggerInvocationSink();
        var pricingApi = new StubHttpMessageHandler();
        pricingApi
            .When(HttpMethod.Get, "/products/43/price")
            .Respond(HttpStatusCode.ServiceUnavailable);
        await using var host = CreatePricingHost(recorder, pricingApi);
        var trigger = KafkaTriggerData.JsonTrigger(
            new OrderPricingRequestedMessage("order-retry", 43, 2),
            topic: "order-pricing");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            host.InvokeAsync<PriceOrderKafkaFunction, string>(
                nameof(PriceOrderKafkaFunction),
                trigger,
                (function, message, context) => function.RunAsync(message, context),
                TestContext.Current.CancellationToken));

        pricingApi.VerifyCalled(HttpMethod.Get, "/products/43/price");
        Assert.Empty(recorder.Invocations);
    }

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

    [Fact]
    public async Task Invocation_pipeline_runs_middleware_and_asserts_multiple_outputs()
    {
        var recorder = new RecordingTriggerInvocationSink();
        var middlewareSteps = new List<string>();
        await using var host = CreateHostBuilder(recorder)
            .UseMiddleware(async (context, next) =>
            {
                middlewareSteps.Add("before");
                context.Items["middleware"] = "visited";
                await next(context);
                middlewareSteps.Add("after");
            })
            .Build();
        var trigger = AzureFunctionTestHost.QueueTrigger()
            .WithJsonBody(new KafkaOrderMessage("order-output", 11))
            .Build();
        var context = trigger.ApplyTo(
            host.CreateContext("RouteOrder", TestContext.Current.CancellationToken)
                .WithRetry(1, 3));

        var invocation = await host.InvokeAsync<AdditionalTriggerFunctions, MultipleBindingOutput>(
            context,
            (function, testContext) => function.RouteOrderAsync(trigger.Value, testContext));

        Assert.True(invocation.FunctionExecuted);
        Assert.Equal(["before", "after"], middlewareSteps);
        Assert.Equal("visited", invocation.Context.Items["middleware"]);
        invocation.Context.Bindings.Should()
            .HaveCount(2)
            .HaveValue(
                "QueueMessage",
                "{\"orderId\":\"order-output\",\"quantity\":11}")
            .HaveValue(
                "BlobDocument",
                "order=order-output;quantity=11;retry=1");
        Assert.Equal(2, invocation.Context.FunctionDefinition.OutputBindings.Count);
        Assert.Equal(
            "queueOutput",
            invocation.Context.FunctionDefinition.OutputBindings["QueueMessage"].Type);
        Assert.Equal(
            "blobOutput",
            invocation.Context.FunctionDefinition.OutputBindings["BlobDocument"].Type);

        var bindings = invocation.Context.Bindings;
        bindings.Should().Contain("QueueMessage").NotContain("missing");
        Assert.Throws<TestOutputBindingVerificationException>(() => bindings.Should().HaveCount(3));
        Assert.Throws<TestOutputBindingVerificationException>(() => bindings.Should().Contain("missing"));
        Assert.Throws<TestOutputBindingVerificationException>(() =>
            bindings.Should().HaveValue("QueueMessage", "different"));
        Assert.Throws<TestOutputBindingVerificationException>(() =>
            bindings.Should().NotContain("QueueMessage"));
        Assert.Throws<ArgumentException>(() => bindings.Should().Contain(" "));
        Assert.Throws<ArgumentException>(() => bindings.Should().NotContain(" "));
        Assert.Throws<KeyNotFoundException>(() => bindings.GetOutput<string>("missing"));
        Assert.Throws<InvalidCastException>(() => bindings.GetOutput<int>("QueueMessage"));
    }

    [Fact]
    public void Function_bindings_capture_null_scalar_and_composite_invocation_results()
    {
        var nullBindings = new TestFunctionBindings();
        nullBindings.CaptureInput("nullable", null);
        nullBindings.CaptureOutput("nullable", null);
        nullBindings.CaptureInvocationResult(null);
        Assert.Null(nullBindings.GetInput<string>("nullable"));
        Assert.Null(nullBindings.GetOutput<string>("nullable"));
        nullBindings.Should().HaveValue<string?>("nullable", null);

        object[] scalars =
        [
            1,
            DayOfWeek.Monday,
            "text",
            1.5m,
            Guid.Empty,
            DateTime.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            TimeSpan.Zero
        ];
        foreach (var scalar in scalars)
        {
            var bindings = new TestFunctionBindings();
            bindings.CaptureInvocationResult(scalar);
            Assert.Same(scalar, bindings.Outputs["$return"]);
            Assert.Equal("return", bindings.OutputTypes["$return"]);
        }

        var composite = new TestFunctionBindings();
        composite.CaptureInvocationResult(new { Value = 42, Text = "answer" });
        Assert.Equal(42, composite.GetOutput<int>("Value"));
        Assert.Equal("answer", composite.GetOutput<string>("Text"));
        Assert.Throws<ArgumentException>(() => composite.GetOutput<int>(" "));
    }

    private static AzureFunctionTestHost CreateHost(RecordingTriggerInvocationSink recorder) =>
        CreateHostBuilder(recorder).Build();

    private static AzureFunctionTestHostBuilder CreateHostBuilder(
        RecordingTriggerInvocationSink recorder) =>
        AzureFunctionTestHost.CreateBuilder()
            .AddFunction<ProcessOrderHttpFunction>()
            .AddFunction<CleanupTimerFunction>()
            .AddFunction<ProcessOrderKafkaFunction>()
            .AddFunction<AdditionalTriggerFunctions>()
            .ConfigureServices(services =>
                services.AddSingleton<ITriggerInvocationSink>(recorder));

    private static AzureFunctionTestHost CreatePricingHost(
        RecordingTriggerInvocationSink recorder,
        StubHttpMessageHandler pricingApi) =>
        AzureFunctionTestHost.CreateBuilder()
            .AddFunction<PriceOrderKafkaFunction>()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ITriggerInvocationSink>(recorder);
                services
                    .AddHttpClient<IOrderPricingClient, OrderPricingClient>(client =>
                        client.BaseAddress = new Uri("https://pricing.example.test/"))
                    .ConfigurePrimaryHttpMessageHandler(() => pricingApi)
                    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            })
            .Build();

    private sealed record InvocationMarker(string Value);
}
