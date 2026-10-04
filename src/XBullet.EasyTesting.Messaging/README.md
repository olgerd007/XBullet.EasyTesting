# XBullet.EasyTesting.Messaging

Transport-neutral recording of messages published by an application under test.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Messaging
```

## Example

Adapt your application's publisher interface to a `RecordedMessageBus`, then assert the captured
transport, destination, headers, and typed payload:

```csharp
await messages.RecordAsync(
    MessageTransportNames.Kafka,
    "orders.created",
    new OrderCreated(42),
    headers,
    cancellationToken);

messages.Should()
    .ContainSingle(MessageTransportNames.Kafka, "orders.created")
    .HaveHeader("partition-key", "customer-7")
    .HavePayload(new OrderCreated(42));
```

Well-known transport names are included for Kafka, Azure Service Bus, and Azure Notification Hubs,
while custom transports remain supported.

Collection assertions support route counts with `HaveCount(transport, destination, expected)`,
message predicates through `Contain` and `ContainSingle`, and absence checks through `NotContain`.
Use `HaveSequence` for exact ordered collections, globally or for one route, and
`HavePayloadMatching<T>` for partial typed payload checks using the recorder's serializer options.
Each assertion uses one stable snapshot; predicate exceptions propagate unchanged. Wait for known
background-work completion before asserting absence. Collection failure diagnostics include
unredacted payloads; use synthetic test data.

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting-messaging.html)
- [Published messages guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/messaging.md)
- [Executable messaging examples](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/tests/XBullet.EasyTesting.Tests/RecordedMessageBusTests.cs)
- [Collection assertion examples](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/tests/XBullet.EasyTesting.Tests/RecordedMessageCollectionAssertionTests.cs)
