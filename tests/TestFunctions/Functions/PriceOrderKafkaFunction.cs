using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using TestFunctions.External;
using TestFunctions.Models;
using TestFunctions.Services;

namespace TestFunctions.Functions;

public sealed class PriceOrderKafkaFunction(
    IOrderPricingClient pricingClient,
    ITriggerInvocationSink sink)
{
    private static JsonSerializerOptions JsonOptions { get; } =
        new(JsonSerializerDefaults.Web);

    [Function(nameof(PriceOrderKafkaFunction))]
    public async Task RunAsync(
        [KafkaTrigger(
            "%KafkaBroker%",
            "order-pricing",
            ConsumerGroup = "test-functions-pricing")]
        string message,
        FunctionContext context)
    {
        var order = JsonSerializer.Deserialize<OrderPricingRequestedMessage>(message, JsonOptions)
            ?? throw new InvalidOperationException("Kafka order-pricing payload is required.");
        var price = await pricingClient.GetProductPriceAsync(
            order.ProductId,
            context.CancellationToken)
            ?? throw new InvalidOperationException(
                $"Pricing was not found for product {order.ProductId}.");

        await sink.RecordAsync(
            new TriggerInvocation(
                "kafka-pricing",
                order.OrderId,
                order.Quantity,
                Detail: price.Currency,
                Amount: price.UnitPrice * order.Quantity),
            context.CancellationToken);
    }
}
