using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Builds an Azure Queue Storage trigger value and message metadata.</summary>
public sealed class QueueTriggerBuilder
{
    private readonly Dictionary<string, object?> _metadata =
        new(StringComparer.OrdinalIgnoreCase);
    private string _bindingName = "message";
    private string _body = string.Empty;

    /// <summary>Sets the input binding name.</summary>
    public QueueTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets the queue message body as text.</summary>
    public QueueTriggerBuilder WithBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _body = body;
        return this;
    }

    /// <summary>Serializes a value as the queue message JSON body.</summary>
    public QueueTriggerBuilder WithJsonBody<T>(T value)
    {
        _body = JsonSerializer.Serialize(value, JsonOptions.Default);
        return this;
    }

    /// <summary>Sets the queue message identifier.</summary>
    public QueueTriggerBuilder WithMessageId(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _metadata["Id"] = messageId;
        return this;
    }

    /// <summary>Sets the dequeue count.</summary>
    public QueueTriggerBuilder WithDequeueCount(int dequeueCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dequeueCount);
        _metadata["DequeueCount"] = dequeueCount;
        return this;
    }

    /// <summary>Adds queue binding metadata.</summary>
    public QueueTriggerBuilder WithMetadata(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a string trigger parameter.</summary>
    public TestTriggerData<string> Build() =>
        new(_body, _bindingName, "queueTrigger", _metadata);
}
