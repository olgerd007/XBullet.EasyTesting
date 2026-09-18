using XBullet.EasyTesting.Messaging;
using TestStartupApi.Messaging;

namespace TestStartupApi.IntegrationTests;

internal sealed class RecordingKafkaPublisher(RecordedMessageBus messages) : IKafkaPublisher
{
    public Task PublishAsync<T>(
        string topic,
        string key,
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var capturedHeaders = headers is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(headers);
        capturedHeaders["partition-key"] = key;
        return messages.RecordAsync("Kafka", topic, message, capturedHeaders, cancellationToken);
    }
}
