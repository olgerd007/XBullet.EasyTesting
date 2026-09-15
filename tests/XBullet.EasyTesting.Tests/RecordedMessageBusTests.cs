using System.Text.Json;
using XBullet.EasyTesting.Messaging;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class RecordedMessageBusTests
{
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

    [Fact]
    public void Assertions_describe_missing_and_ambiguous_messages_and_headers()
    {
        var recorder = new RecordedMessageBus();

        var missing = Assert.Throws<RecordedMessageVerificationException>(
            () => recorder.Should().ContainSingle(MessageTransportNames.Kafka, "orders.created"));

        Assert.Contains("found 0", missing.Message);
        Assert.Contains("No messages were recorded", missing.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Should().HaveCount(-1));

        recorder.Record(MessageTransportNames.Kafka, "orders.created", new Message(42, "Created"));
        recorder.Record(MessageTransportNames.Kafka, "orders.created", new Message(43, "Created"));

        var ambiguous = Assert.Throws<RecordedMessageVerificationException>(
            () => recorder.Should().ContainSingle(MessageTransportNames.Kafka, "orders.created"));

        Assert.Contains("found 2", ambiguous.Message);
        Assert.Contains("Recorded messages", ambiguous.Message);

        recorder.Reset();
        recorder.Record(
            MessageTransportNames.Kafka,
            "orders.created",
            new Message(42, "Created"),
            new Dictionary<string, string> { ["partition-key"] = "customer-7" });
        var message = recorder.Should()
            .ContainSingle(MessageTransportNames.Kafka, "orders.created");

        Assert.Same(message, message.HaveHeader("partition-key"));
        var missingHeader = Assert.Throws<RecordedMessageVerificationException>(
            () => message.HaveHeader("missing"));
        var missingHeaderValue = Assert.Throws<RecordedMessageVerificationException>(
            () => message.HaveHeader("missing", "value"));

        Assert.Contains("missing", missingHeader.Message);
        Assert.Contains("missing", missingHeaderValue.Message);
        Assert.Throws<ArgumentException>(() => message.HaveHeader(" "));
        Assert.Throws<ArgumentNullException>(() => message.HaveHeader("partition-key", null!));
    }

    [Fact]
    public void Payload_assertion_uses_the_recorders_serializer_options()
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        };
        var recorder = new RecordedMessageBus(serializerOptions);
        recorder.Record(
            MessageTransportNames.Kafka,
            "orders.created",
            new Message(42, "Created"));

        var message = recorder.Should()
            .ContainSingle(MessageTransportNames.Kafka, "orders.created");

        Assert.Same(message, message.HavePayload(new Message(42, "Created")));
    }

    private sealed record Message(int Id, string State);
}
