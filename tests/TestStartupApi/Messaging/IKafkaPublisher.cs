namespace TestStartupApi.Messaging;

public interface IKafkaPublisher
{
    Task PublishAsync<T>(
        string topic,
        string key,
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
