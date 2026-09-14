namespace TestFunctions.Models;

public sealed record CreateOrderRequest(string OrderId, int Quantity);

public sealed record AcceptedOrderResponse(string OrderId, int Quantity, string Status);

public sealed record KafkaOrderMessage(string OrderId, int Quantity);

public sealed record TriggerInvocation(
    string Trigger,
    string Subject,
    int Quantity,
    bool IsPastDue = false);
