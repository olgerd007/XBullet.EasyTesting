using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace TestStartupApi.Messaging;

public sealed class ConfluentKafkaPublisher(IProducer<string, string> producer) : IKafkaPublisher
{
    public async Task PublishAsync<T>(
        string topic,
        string key,
        T message,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var kafkaHeaders = new Headers();
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                kafkaHeaders.Add(header.Key, Encoding.UTF8.GetBytes(header.Value));
            }
        }

        await producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = key,
                Value = JsonSerializer.Serialize(message),
                Headers = kafkaHeaders
            },
            cancellationToken);
    }
}
