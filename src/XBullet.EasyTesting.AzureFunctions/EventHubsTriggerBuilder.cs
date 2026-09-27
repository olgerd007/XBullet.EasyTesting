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
    /// <param name="bindingName">The non-empty worker input name. The default is <c>events</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventHubsTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Adds a text event to the batch.</summary>
    /// <param name="body">The non-null event body; empty text is accepted.</param>
    /// <returns>This builder, for chaining. Events retain insertion order.</returns>
    public EventHubsTriggerBuilder AddEvent(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _events.Add(body);
        return this;
    }

    /// <summary>Serializes and adds a JSON event to the batch.</summary>
    /// <typeparam name="T">The value type serialized with the package's web JSON defaults.</typeparam>
    /// <param name="value">The value serialized immediately; null is emitted when permitted by <typeparamref name="T"/>.</param>
    /// <returns>This builder, for chaining. Events retain insertion order.</returns>
    public EventHubsTriggerBuilder AddJsonEvent<T>(T value)
    {
        _events.Add(JsonSerializer.Serialize(value, JsonOptions.Default));
        return this;
    }

    /// <summary>Sets the partition identifier.</summary>
    /// <param name="partitionId">The non-empty identifier stored as <c>PartitionId</c> metadata.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventHubsTriggerBuilder WithPartitionId(string partitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionId);
        _metadata["PartitionId"] = partitionId;
        return this;
    }

    /// <summary>Adds Event Hubs binding metadata.</summary>
    /// <param name="name">The non-empty metadata key, matched case-insensitively.</param>
    /// <param name="value">The value retained without cloning; <see langword="null"/> is accepted.</param>
    /// <returns>This builder, for chaining. An existing key is replaced.</returns>
    public EventHubsTriggerBuilder WithMetadata(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _metadata[name] = value;
        return this;
    }

    /// <summary>Builds trigger data whose value can be passed to a string-array trigger parameter.</summary>
    /// <returns>A new trigger containing a copied event array and the current binding metadata.</returns>
    public TestTriggerData<string[]> Build() =>
        new([.. _events], _bindingName, "eventHubTrigger", _metadata);
}
