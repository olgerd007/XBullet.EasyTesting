namespace TestStartupApi.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string OrdersImportedTopic { get; set; } = "orders.imported";
}
