using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Builds a Service Bus trigger value and broker metadata.</summary>
public sealed class ServiceBusTriggerBuilder
{
    private readonly Dictionary<string, object?> _metadata =
        new(StringComparer.OrdinalIgnoreCase);
    private string _bindingName = "message";
    private string _body = string.Empty;

    /// <summary>Sets the input binding name.</summary>
    public ServiceBusTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets the message body as text.</summary>
    public ServiceBusTriggerBuilder WithBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _body = body;
        return this;
    }

    /// <summary>Serializes a value as the message JSON body.</summary>
    public ServiceBusTriggerBuilder WithJsonBody<T>(T value)
    {
        _body = JsonSerializer.Serialize(value, JsonOptions.Default);
        return this;
    }

    /// <summary>Sets the Service Bus message identifier.</summary>
    public ServiceBusTriggerBuilder WithMessageId(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _metadata["MessageId"] = messageId;
        return this;
    }

    /// <summary>Sets the Service Bus correlation identifier.</summary>
    public ServiceBusTriggerBuilder WithCorrelationId(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        _metadata["CorrelationId"] = correlationId;
        return this;
    }

    /// <summary>Adds application or broker metadata.</summary>
    public ServiceBusTriggerBuilder WithProperty(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a string trigger parameter.</summary>
    public TestTriggerData<string> Build() =>
        new(_body, _bindingName, "serviceBusTrigger", _metadata);
}
