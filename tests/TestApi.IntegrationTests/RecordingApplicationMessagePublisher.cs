using XBullet.EasyTesting.Messaging;
using TestApi.Messaging;

namespace TestApi.IntegrationTests;

internal sealed class RecordingApplicationMessagePublisher(
    RecordedMessageBus messages) : IApplicationMessagePublisher
{
    public Task PublishAsync<T>(
        string transport,
        string destination,
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default) =>
        messages.RecordAsync(transport, destination, message, headers, cancellationToken);
}
