using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Creates payloads for Kafka-trigger function tests.</summary>
public static class KafkaTriggerData
{
    /// <summary>Serializes one Kafka message value as JSON.</summary>
    public static string Json<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(
            value,
            options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Serializes a batch of Kafka message values as JSON strings.</summary>
    public static string[] JsonBatch<T>(
        IEnumerable<T> values,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Select(value => Json(value, options)).ToArray();
    }

    /// <summary>Creates one JSON message together with capturable Kafka trigger metadata.</summary>
    public static TestTriggerData<string> JsonTrigger<T>(
        T value,
        string bindingName = "message",
        string? topic = null,
        string? partitionKey = null,
        JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        var metadata = new Dictionary<string, object?>();
        if (topic is not null)
        {
            metadata["Topic"] = topic;
        }

        if (partitionKey is not null)
        {
            metadata["PartitionKey"] = partitionKey;
        }

        return new TestTriggerData<string>(
            Json(value, options),
            bindingName,
            "kafkaTrigger",
            metadata);
    }

    /// <summary>Creates a JSON batch together with capturable Kafka trigger metadata.</summary>
    public static TestTriggerData<string[]> JsonBatchTrigger<T>(
        IEnumerable<T> values,
        string bindingName = "messages",
        string? topic = null,
        JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        var metadata = new Dictionary<string, object?>();
        if (topic is not null)
        {
            metadata["Topic"] = topic;
        }

        return new TestTriggerData<string[]>(
            JsonBatch(values, options),
            bindingName,
            "kafkaTrigger",
            metadata);
    }
}
