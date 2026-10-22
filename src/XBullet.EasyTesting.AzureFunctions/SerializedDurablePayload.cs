using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

// Keep the serialized envelope internal so recordings still expose the caller's original arguments.
internal sealed class SerializedDurablePayload(object? value, DataConverter converter)
{
    public string? Data { get; } = converter.Serialize(value);

    public T Read<T>()
    {
        var result = converter.Deserialize(Data, typeof(T));
        return result is T typed ? typed : result is null && default(T) is null ? default!
            : throw new InvalidOperationException($"The payload converter did not return a value assignable to {typeof(T).FullName}.");
    }

    public static object? Capture(object? value, DataConverter? converter) =>
        converter is null || value is SerializedDurablePayload ? value : new SerializedDurablePayload(value, converter);
}
