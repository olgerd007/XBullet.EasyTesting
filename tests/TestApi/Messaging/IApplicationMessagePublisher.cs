namespace TestApi.Messaging;

public interface IApplicationMessagePublisher
{
    Task PublishAsync<T>(
        string transport,
        string destination,
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
