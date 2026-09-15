using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Builds an Event Hubs trigger batch and partition metadata.</summary>
public sealed class EventHubsTriggerBuilder
{
    private readonly List<string> _events = [];
    private readonly Dictionary<string, object?> _metadata =
        new(StringComparer.OrdinalIgnoreCase);
    private string _bindingName = "events";

    /// <summary>Sets the input binding name.</summary>
    public EventHubsTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Adds a text event to the batch.</summary>
    public EventHubsTriggerBuilder AddEvent(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _events.Add(body);
        return this;
    }

    /// <summary>Serializes and adds a JSON event to the batch.</summary>
    public EventHubsTriggerBuilder AddJsonEvent<T>(T value)
    {
        _events.Add(JsonSerializer.Serialize(value, JsonOptions.Default));
        return this;
    }

    /// <summary>Sets the partition identifier.</summary>
    public EventHubsTriggerBuilder WithPartitionId(string partitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionId);
        _metadata["PartitionId"] = partitionId;
        return this;
    }

    /// <summary>Adds Event Hubs binding metadata.</summary>
    public EventHubsTriggerBuilder WithMetadata(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a string-array trigger parameter.</summary>
    public TestTriggerData<string[]> Build() =>
        new([.. _events], _bindingName, "eventHubTrigger", _metadata);
}
