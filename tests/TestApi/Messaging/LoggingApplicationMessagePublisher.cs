namespace TestApi.Messaging;

public sealed class LoggingApplicationMessagePublisher(
    ILogger<LoggingApplicationMessagePublisher> logger) : IApplicationMessagePublisher
{
    public Task PublishAsync<T>(
        string transport,
        string destination,
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Publishing {MessageType} to {Transport}/{Destination}",
            typeof(T).Name,
            transport,
            destination);
        return Task.CompletedTask;
    }
}
