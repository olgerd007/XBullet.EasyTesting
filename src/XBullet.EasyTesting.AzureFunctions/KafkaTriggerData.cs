using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Creates payloads for Kafka-trigger function tests.</summary>
public static class KafkaTriggerData
{
    /// <summary>Serializes one Kafka message value as JSON.</summary>
    /// <typeparam name="T">The message value type.</typeparam>
    /// <param name="value">The value serialized immediately; null is emitted when <typeparamref name="T"/> permits it.</param>
    /// <param name="options">JSON options to use, or <see langword="null"/> for new web defaults.</param>
    /// <returns>The serialized JSON message text.</returns>
    public static string Json<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(
            value,
            options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Serializes a batch of Kafka message values as JSON strings.</summary>
    /// <typeparam name="T">The message value type.</typeparam>
    /// <param name="values">The non-null sequence enumerated and serialized immediately in order.</param>
    /// <param name="options">JSON options to use for every value, or <see langword="null"/> for web defaults.</param>
    /// <returns>A newly allocated array of serialized messages; an empty sequence produces an empty array.</returns>
    public static string[] JsonBatch<T>(
        IEnumerable<T> values,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Select(value => Json(value, options)).ToArray();
    }

    /// <summary>Creates one JSON message together with capturable Kafka trigger metadata.</summary>
    /// <typeparam name="T">The message value type.</typeparam>
    /// <param name="value">The value serialized immediately as JSON.</param>
    /// <param name="bindingName">The non-empty worker input name. The default is <c>message</c>.</param>
    /// <param name="topic">Optional topic metadata; <see langword="null"/> omits it and empty text is retained.</param>
    /// <param name="partitionKey">Optional partition-key metadata; <see langword="null"/> omits it and empty text is retained.</param>
    /// <param name="options">JSON options to use, or <see langword="null"/> for web defaults.</param>
    /// <returns>New string trigger data with Kafka binding type and the supplied optional metadata.</returns>
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
    /// <typeparam name="T">The message value type.</typeparam>
    /// <param name="values">The non-null sequence enumerated and serialized immediately in order.</param>
    /// <param name="bindingName">The non-empty worker input name. The default is <c>messages</c>.</param>
    /// <param name="topic">Optional topic metadata; <see langword="null"/> omits it and empty text is retained.</param>
    /// <param name="options">JSON options used for every item, or <see langword="null"/> for web defaults.</param>
    /// <returns>New string-array trigger data with Kafka binding type and optional topic metadata.</returns>
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
