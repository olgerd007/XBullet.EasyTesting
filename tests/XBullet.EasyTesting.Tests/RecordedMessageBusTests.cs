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

        var recorded = Assert.Single(recorder.For(MessageTransportNames.Kafka, "orders.created"));
        Assert.Equal("customer-7", recorded.Headers["partition-key"]);
        Assert.Equal(new Message(42, "Created"), recorded.GetPayload<Message>());
        Assert.Equal(1, recorder.Count);

        Assert.Same(recorder, recorder.Reset());
        Assert.Empty(recorder.Messages);
    }

    private sealed record Message(int Id, string State);
}
