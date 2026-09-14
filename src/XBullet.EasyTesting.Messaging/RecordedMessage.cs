using System.Text.Json;

namespace XBullet.EasyTesting.Messaging;

/// <summary>A transport-neutral representation of one successfully published message.</summary>
public sealed record RecordedMessage(
    string Transport,
    string Destination,
    string MessageType,
    JsonElement Payload,
    IReadOnlyDictionary<string, string> Headers)
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Deserializes the captured JSON payload to the requested type.</summary>
    public T? GetPayload<T>(JsonSerializerOptions? serializerOptions = null) =>
        Payload.Deserialize<T>(serializerOptions ?? DefaultSerializerOptions);
}
