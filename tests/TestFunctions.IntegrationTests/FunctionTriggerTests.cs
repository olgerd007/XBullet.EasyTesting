using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TestFunctions.Functions;
using TestFunctions.Models;
using TestFunctions.Services;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class FunctionTriggerTests
{
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
        var function = host.GetRequiredService<CleanupTimerFunction>();
        var timer = AzureFunctionTestHost.Timer()
            .PastDue()
            .WithSchedule(
                new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 14, 9, 5, 0, DateTimeKind.Utc))
            .Build();

        await function.RunAsync(
            timer,
            host.CreateContext(
                nameof(CleanupTimerFunction),
                TestContext.Current.CancellationToken));

        Assert.True(timer.IsPastDue);
        Assert.NotNull(timer.ScheduleStatus);
        Assert.Equal(
            new TriggerInvocation("timer", "cleanup", 0, IsPastDue: true),
            Assert.Single(recorder.Invocations));
    }

    [Fact]
    public async Task Kafka_trigger_deserializes_and_records_an_order()
    {
        var recorder = new RecordingTriggerInvocationSink();
        await using var host = CreateHost(recorder);
        var function = host.GetRequiredService<ProcessOrderKafkaFunction>();
        var message = KafkaTriggerData.Json(new KafkaOrderMessage("order-kafka", 7));

        await function.RunAsync(
            message,
            host.CreateContext(
                nameof(ProcessOrderKafkaFunction),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            new TriggerInvocation("kafka", "order-kafka", 7),
            Assert.Single(recorder.Invocations));
    }

    private static AzureFunctionTestHost CreateHost(RecordingTriggerInvocationSink recorder) =>
        AzureFunctionTestHost.CreateBuilder()
            .AddFunction<ProcessOrderHttpFunction>()
            .AddFunction<CleanupTimerFunction>()
            .AddFunction<ProcessOrderKafkaFunction>()
            .ConfigureServices(services =>
                services.AddSingleton<ITriggerInvocationSink>(recorder))
            .Build();
}
