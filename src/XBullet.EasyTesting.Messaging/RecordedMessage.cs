using System.Text.Json;

namespace XBullet.EasyTesting.Messaging;

/// <summary>A transport-neutral representation of one successfully published message.</summary>
/// <param name="Transport">The transport identifier, such as <c>Kafka</c>.</param>
/// <param name="Destination">
/// The transport-specific queue, topic, hub, or other destination name.
/// </param>
/// <param name="MessageType">
/// The captured compile-time payload type name, normally its fully qualified name.
/// </param>
/// <param name="Payload">
/// The self-contained JSON element produced when the message was recorded.
/// </param>
/// <param name="Headers">
/// The captured message headers. Recorder-created dictionaries use case-insensitive keys. Header
/// values are stored without automatic redaction.
/// </param>
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
    /// <typeparam name="T">The target payload type.</typeparam>
    /// <param name="serializerOptions">
    /// Options used for deserialization, or <see langword="null"/> to use new web-default options.
    /// The supplied instance is read but not owned, retained, or mutated by this method.
    /// </param>
    /// <returns>
    /// The deserialized payload, or <see langword="null"/> when the captured JSON represents null
    /// or the target type's converter returns null.
    /// </returns>
    public T? GetPayload<T>(JsonSerializerOptions? serializerOptions = null) =>
        Payload.Deserialize<T>(serializerOptions ?? DefaultSerializerOptions);
}
