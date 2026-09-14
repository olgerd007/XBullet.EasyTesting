namespace XBullet.EasyTesting.Messaging;

/// <summary>Well-known transport names for recorded integration-test messages.</summary>
public static class MessageTransportNames
{
    /// <summary>Apache Kafka.</summary>
    public const string Kafka = "Kafka";

    /// <summary>Azure Service Bus queues and topics.</summary>
    public const string AzureServiceBus = "AzureServiceBus";

    /// <summary>Azure Notification Hubs.</summary>
    public const string AzureNotificationHubs = "AzureNotificationHubs";
}
