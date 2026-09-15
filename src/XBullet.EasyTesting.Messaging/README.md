# XBullet.EasyTesting.Messaging

Transport-neutral recording of messages published by an application under test.

The package targets .NET 8 and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Messaging
```

Adapt your application's publisher interface to a `RecordedMessageBus`, then assert the captured transport, destination, headers, and typed payload:

```csharp
await messages.RecordAsync(
    MessageTransportNames.Kafka,
    "orders.created",
    new OrderCreated(42),
    headers,
    cancellationToken);

var published = Assert.Single(
    messages.For(MessageTransportNames.Kafka, "orders.created"));
var payload = published.GetPayload<OrderCreated>();
```

Well-known transport names are included for Kafka, Azure Service Bus, and Azure Notification Hubs, while custom transports remain supported.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for registration and assertion examples.
