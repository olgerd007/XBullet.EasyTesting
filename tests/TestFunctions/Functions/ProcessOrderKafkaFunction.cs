using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using TestFunctions.Models;
using TestFunctions.Services;

namespace TestFunctions.Functions;

public sealed class ProcessOrderKafkaFunction(ITriggerInvocationSink sink)
{
    [Function(nameof(ProcessOrderKafkaFunction))]
    public async Task RunAsync(
        [KafkaTrigger("%KafkaBroker%", "orders", ConsumerGroup = "test-functions")] string message,
        FunctionContext context)
    {
        var order = JsonSerializer.Deserialize<KafkaOrderMessage>(
            message,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Kafka order payload is required.");

        await sink.RecordAsync(
            new TriggerInvocation("kafka", order.OrderId, order.Quantity),
            context.CancellationToken);
    }
}
