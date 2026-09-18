using Microsoft.Azure.Functions.Worker;

namespace TestFunctions.Models;

public sealed record CreateOrderRequest(string OrderId, int Quantity);

public sealed record AcceptedOrderResponse(string OrderId, int Quantity, string Status);

public sealed record KafkaOrderMessage(string OrderId, int Quantity);

public sealed record OrderPricingRequestedMessage(
    string OrderId,
    int ProductId,
    int Quantity);

public sealed record EventGridOrderEnvelope(KafkaOrderMessage Data);

public sealed record MultipleBindingOutput(
    [property: QueueOutput("processed-orders", Connection = "StorageConnection")]
    string QueueMessage,
    [property: BlobOutput("processed-orders/{rand-guid}.txt", Connection = "StorageConnection")]
    string BlobDocument);

public sealed record TriggerInvocation(
    string Trigger,
    string Subject,
    int Quantity,
    bool IsPastDue = false,
    string? Detail = null,
    decimal? Amount = null);
