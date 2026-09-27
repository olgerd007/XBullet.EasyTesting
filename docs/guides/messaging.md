# Test published messages

Use `XBullet.EasyTesting.Messaging` to test what an application publishes without starting a broker.
`RecordedMessageBus` is transport-neutral: production code keeps its publisher abstraction, while
the test adapter records the transport, destination, serialized payload, and headers.

## Install and create an adapter

```shell
dotnet add package XBullet.EasyTesting.Messaging
```

Import `XBullet.EasyTesting.Messaging`; hosted examples also use
`XBullet.EasyTesting.Hosting` and `Microsoft.Extensions.DependencyInjection.Extensions`.

Adapt the application's publisher rather than depending on a broker SDK in controller tests:

```csharp
internal sealed class RecordingPublisher(RecordedMessageBus messages)
    : IApplicationMessagePublisher
{
    public Task PublishAsync<T>(
        string transport,
        string destination,
        T message,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken) =>
        messages.RecordAsync(transport, destination, message, headers, cancellationToken);
}
```

Replace the production adapter in the test factory and expose one recorder:

```csharp
public RecordedMessageBus PublishedMessages { get; } = new();

protected override void ConfigureAdditionalServicesForTests(IServiceCollection services)
{
    services.RemoveAll<IApplicationMessagePublisher>();
    services.AddSingleton<IApplicationMessagePublisher>(
        new RecordingPublisher(PublishedMessages));
}
```

Register a shared recorder as a scenario resource. This resets it before each scenario, prevents
messages from leaking between tests, and includes recorded metadata in failure diagnostics.

## Record and assert a message

<!-- snippet: tests/XBullet.EasyTesting.Tests/RecordedMessageBusTests.cs#docs-message-recording -->
```csharp
[Fact]
public async Task Recorder_captures_serialized_payload_headers_and_destination()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var recorder = new RecordedMessageBus();
    var headers = new Dictionary<string, string>
    {
        ["partition-key"] = "customer-7"
    };

    await recorder.RecordAsync(
        MessageTransportNames.Kafka,
        "orders.created",
        new Message(42, "Created"),
        headers,
        cancellationToken);
    headers["partition-key"] = "changed-after-publication";

    recorder.Should()
        .HaveCount(1)
        .ContainSingle(MessageTransportNames.Kafka, "orders.created")
        .HaveHeader("partition-key", "customer-7")
        .HavePayload(new Message(42, "Created"));

    Assert.Same(recorder, recorder.Reset());
    Assert.Empty(recorder.Messages);
}
```
<!-- end-snippet -->

The recorder serializes and copies payload and header values at publication time, so later mutation
does not rewrite history. Pass custom `JsonSerializerOptions` to its constructor when assertions
must follow an application's JSON naming policy.

## Kafka, Service Bus, Notification Hubs, and custom transports

`MessageTransportNames` defines stable names for Kafka, Azure Service Bus, and Azure Notification
Hubs. Destinations remain application-defined: for example a Kafka topic, Service Bus queue or
topic, or notification hub. Any non-empty transport name is allowed, so the same adapter pattern
also works for RabbitMQ, Event Hubs, SNS/SQS, email, webhooks, or an internal dispatcher.

This controller test verifies a Kafka destination, broker-specific headers, and the typed payload:

<!-- snippet: tests/TestApi.IntegrationTests/PublishingControllerTests.cs#docs-kafka-publishing -->
```csharp
[Fact]
public Task Controller_publishes_order_to_kafka_topic() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = CreateAuthenticatedClient(scope);
        using var response = await client.PostAsJsonAsync(
            "/api/publishing/kafka/orders",
            new { OrderId = 42, CustomerId = "customer-7", Total = 125.50m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var recorded = Assert.Single(
            _factory.PublishedMessages.For(MessageTransportNames.Kafka, "orders.created"));
        Assert.Equal("order-created", recorded.Headers["event-type"]);
        Assert.Equal("customer-7", recorded.Headers["partition-key"]);
        Assert.Equal(
            new OrderCreatedMessage(42, "customer-7", 125.50m),
            recorded.GetPayload<OrderCreatedMessage>());
    });
```
<!-- end-snippet -->

The same test suite contains executable
[Service Bus and Notification Hubs examples](../../tests/TestApi.IntegrationTests/PublishingControllerTests.cs).
Only the application adapter changes; assertions remain transport-neutral.

## Counts, filtering, payloads, and order

- `Count` and `HaveCount` assert total publication count.
- `For(transport, destination)` filters recorded messages.
- `ContainSingle(transport, destination)` selects exactly one matching message.
- `HaveHeader(name)` and `HaveHeader(name, value)` verify metadata.
- `HavePayload<T>()` deserializes a payload; `HavePayload(expected)` compares serialized structure.
- `Messages` preserves publication order. Use indexed or sequence assertions when order is part of
  the contract; otherwise filter by transport and destination to avoid brittle tests.

Avoid asserting incidental broker headers generated below the application boundary. Use a broker
or container integration test when SDK serialization, partition assignment, settlement, delivery,
or broker configuration is the behavior under test.

## Negative behavior and diagnostics

A rejected request should normally publish nothing:

<!-- snippet: tests/TestApi.IntegrationTests/PublishingControllerTests.cs#docs-message-negative-path -->
```csharp
[Fact]
public Task Invalid_request_does_not_publish_a_message() =>
    Run(async (scope, cancellationToken) =>
    {
        using var client = CreateAuthenticatedClient(scope);
        using var response = await client.PostAsJsonAsync(
            "/api/publishing/kafka/orders",
            new { OrderId = 42, CustomerId = string.Empty, Total = 0m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.PublishedMessages.Count);
    });
```
<!-- end-snippet -->

Assertion exceptions report expected and actual counts and include recorded message summaries.
Header failures name the missing or different header; payload failures include expected and actual
JSON:

<!-- snippet: tests/XBullet.EasyTesting.Tests/RecordedMessageBusTests.cs#docs-message-diagnostics -->
```csharp
[Fact]
public void Assertions_describe_message_count_header_and_payload_mismatches()
{
    var recorder = new RecordedMessageBus();
    recorder.Record(
        MessageTransportNames.Kafka,
        "orders.created",
        new Message(42, "Created"),
        new Dictionary<string, string> { ["partition-key"] = "customer-7" });

    var countFailure = Assert.Throws<RecordedMessageVerificationException>(
        () => recorder.Should().HaveCount(2));
    var message = recorder.Should()
        .ContainSingle(MessageTransportNames.Kafka, "orders.created");
    var headerFailure = Assert.Throws<RecordedMessageVerificationException>(
        () => message.HaveHeader("partition-key", "customer-8"));
    var payloadFailure = Assert.Throws<RecordedMessageVerificationException>(
        () => message.HavePayload(new Message(43, "Failed")));

    Assert.Contains("found 1", countFailure.Message);
    Assert.Contains("customer-8", headerFailure.Message);
    Assert.Contains("\"id\": 43", payloadFailure.Message);
    Assert.Contains("\"id\": 42", payloadFailure.Message);
}
```
<!-- end-snippet -->

Payload diagnostics can contain business data. Use synthetic test values and avoid putting secrets
in message payloads or headers. See the canonical
[`RecordedMessageBusTests`](../../tests/XBullet.EasyTesting.Tests/RecordedMessageBusTests.cs) for
missing-message, ambiguous-message, and custom-serializer cases.

Browse the [messaging API reference](../api/packages/xbullet-easytesting-messaging.md).
