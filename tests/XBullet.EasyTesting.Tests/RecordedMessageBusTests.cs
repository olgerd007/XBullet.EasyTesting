using System.Text.Json;
using XBullet.EasyTesting.Messaging;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class RecordedMessageBusTests
{
    [Fact]
    public void Concurrent_records_are_indexed_by_transport_and_destination()
    {
        var recorder = new RecordedMessageBus();

        Parallel.For(0, 1_000, index => recorder.Record(
            index % 2 == 0 ? MessageTransportNames.Kafka : "kAfKa",
            index % 4 < 2 ? "orders.created" : "orders.updated",
            new Message(index, "Recorded")));

        Assert.Equal(1_000, recorder.Count);
        Assert.Equal(500, recorder.For(MessageTransportNames.Kafka, "orders.created").Count);
        Assert.Equal(500, recorder.For(MessageTransportNames.Kafka, "orders.updated").Count);

        recorder.Reset();
        Assert.Empty(recorder.For(MessageTransportNames.Kafka, "orders.created"));
    }

    [Fact]
    public async Task Diagnostic_count_and_messages_remain_consistent_during_concurrent_record_and_reset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new RecordedMessageBus();
        using var started = new ManualResetEventSlim();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writer = Task.Factory.StartNew(() =>
        {
            var index = 0;
            started.Set();
            while (!stop.IsCancellationRequested)
            {
                recorder.Record(MessageTransportNames.Kafka, "orders.created", new Message(index++, "Created"));
                recorder.Reset();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            started.Wait(cancellationToken);
            for (var index = 0; index < 50_000; index++)
            {
                var diagnostics = await recorder.CaptureDiagnosticsAsync(cancellationToken);
                var snapshot = JsonSerializer.SerializeToElement(diagnostics);

                Assert.Equal(
                    snapshot.GetProperty("Messages").GetArrayLength(),
                    snapshot.GetProperty("Count").GetInt32());
            }
        }
        finally
        {
            stop.Cancel();
            await writer;
        }
    }

    [Fact]
    public async Task Diagnostic_snapshot_survives_reset_and_subsequent_recording()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record(MessageTransportNames.Kafka, "orders.created", new Message(42, "Created"));
        var diagnostics = await recorder.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken);

        recorder.Reset();
        recorder.Record(MessageTransportNames.AzureServiceBus, "invoices", new Message(84, "Requested"));
        var snapshot = JsonSerializer.SerializeToElement(diagnostics);

        Assert.Equal(1, snapshot.GetProperty("Count").GetInt32());
        var message = Assert.Single(snapshot.GetProperty("Messages").EnumerateArray());
        Assert.Equal(MessageTransportNames.Kafka, message.GetProperty("Transport").GetString());
        Assert.Equal("orders.created", message.GetProperty("Destination").GetString());
        Assert.Equal(42, message.GetProperty("Payload").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Diagnostic_capture_observes_cancellation_without_changing_messages()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record(MessageTransportNames.Kafka, "orders.created", new Message(42, "Created"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await recorder.CaptureDiagnosticsAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, recorder.Count);
        Assert.Equal(new Message(42, "Created"), Assert.Single(recorder.Messages).GetPayload<Message>());
    }

    #region docs-message-recording

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

    #endregion

    #region docs-message-diagnostics

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

    #endregion

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
