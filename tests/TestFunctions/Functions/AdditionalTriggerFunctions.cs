using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using TestFunctions.Models;
using TestFunctions.Services;

namespace TestFunctions.Functions;

public sealed class AdditionalTriggerFunctions(ITriggerInvocationSink sink)
{
    [Function("ProcessOrderServiceBus")]
    public Task RunServiceBusAsync(
        [ServiceBusTrigger("orders", Connection = "ServiceBusConnection")] string message,
        FunctionContext context) =>
        RecordAsync("service-bus", DeserializeOrder(message), context);

    [Function("ProcessOrderQueue")]
    public Task RunQueueAsync(
        [QueueTrigger("orders", Connection = "StorageConnection")] string message,
        FunctionContext context) =>
        RecordAsync("queue", DeserializeOrder(message), context);

    [Function("ProcessOrderBlob")]
    public async Task RunBlobAsync(
        [BlobTrigger("orders/{name}", Connection = "StorageConnection")] Stream blob,
        FunctionContext context)
    {
        using var reader = new StreamReader(blob, leaveOpen: true);
        var message = await reader.ReadToEndAsync(context.CancellationToken);
        await RecordAsync("blob", DeserializeOrder(message), context);
    }

    [Function("ProcessOrderEventGrid")]
    public Task RunEventGridAsync(
        [EventGridTrigger] string eventJson,
        FunctionContext context)
    {
        var envelope = JsonSerializer.Deserialize<EventGridOrderEnvelope>(eventJson, JsonOptions)
            ?? throw new InvalidOperationException("Event Grid payload is required.");
        return RecordAsync("event-grid", envelope.Data, context);
    }

    [Function("ProcessOrderEventHubs")]
    public async Task RunEventHubsAsync(
        [EventHubTrigger("orders", Connection = "EventHubsConnection")] string[] messages,
        FunctionContext context)
    {
        foreach (var message in messages)
        {
            await RecordAsync("event-hubs", DeserializeOrder(message), context);
        }
    }

    [Function("RouteOrder")]
    public Task<MultipleBindingOutput> RouteOrderAsync(
        [QueueTrigger("incoming-orders", Connection = "StorageConnection")] string message,
        FunctionContext context)
    {
        var order = DeserializeOrder(message);
        return Task.FromResult(new MultipleBindingOutput(
            JsonSerializer.Serialize(order, JsonOptions),
            $"order={order.OrderId};quantity={order.Quantity};retry={context.RetryContext.RetryCount}"));
    }

    private static JsonSerializerOptions JsonOptions { get; } =
        new(JsonSerializerDefaults.Web);

    private static KafkaOrderMessage DeserializeOrder(string message) =>
        JsonSerializer.Deserialize<KafkaOrderMessage>(message, JsonOptions)
        ?? throw new InvalidOperationException("Order payload is required.");

    private Task RecordAsync(
        string trigger,
        KafkaOrderMessage order,
        FunctionContext context) =>
        sink.RecordAsync(
            new TriggerInvocation(trigger, order.OrderId, order.Quantity),
            context.CancellationToken);
}
