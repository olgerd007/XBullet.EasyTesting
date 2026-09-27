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
    /// <param name="bindingName">The non-empty worker input name. The default is <c>message</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public QueueTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets the queue message body as text.</summary>
    /// <param name="body">The non-null body retained as text; an empty body is accepted.</param>
    /// <returns>This builder, for chaining, replacing the prior body.</returns>
    public QueueTriggerBuilder WithBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _body = body;
        return this;
    }

    /// <summary>Serializes a value as the queue message JSON body.</summary>
    /// <typeparam name="T">The value type serialized with the package's web JSON defaults.</typeparam>
    /// <param name="value">The value serialized immediately; null is emitted when permitted by <typeparamref name="T"/>.</param>
    /// <returns>This builder, for chaining, replacing the prior body.</returns>
    public QueueTriggerBuilder WithJsonBody<T>(T value)
    {
        _body = JsonSerializer.Serialize(value, JsonOptions.Default);
        return this;
    }

    /// <summary>Sets the queue message identifier.</summary>
    /// <param name="messageId">The non-empty identifier stored as <c>Id</c> binding metadata.</param>
    /// <returns>This builder, for chaining.</returns>
    public QueueTriggerBuilder WithMessageId(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _metadata["Id"] = messageId;
        return this;
    }

    /// <summary>Sets the dequeue count.</summary>
    /// <param name="dequeueCount">The non-negative delivery-attempt count.</param>
    /// <returns>This builder, for chaining.</returns>
    public QueueTriggerBuilder WithDequeueCount(int dequeueCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dequeueCount);
        _metadata["DequeueCount"] = dequeueCount;
        return this;
    }

    /// <summary>Adds queue binding metadata.</summary>
    /// <param name="name">The non-empty metadata key, matched case-insensitively.</param>
    /// <param name="value">The value retained without cloning; <see langword="null"/> is accepted.</param>
    /// <returns>This builder, for chaining. An existing key is replaced.</returns>
    public QueueTriggerBuilder WithMetadata(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a string trigger parameter.</summary>
    /// <returns>New trigger data containing the current text body, binding name, and metadata.</returns>
    public TestTriggerData<string> Build() =>
        new(_body, _bindingName, "queueTrigger", _metadata);
}
