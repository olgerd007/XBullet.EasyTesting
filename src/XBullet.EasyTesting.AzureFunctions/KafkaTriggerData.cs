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
}
