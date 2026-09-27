using System.Text.Json;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Builds a CloudEvents-shaped Event Grid trigger value.</summary>
public sealed class EventGridTriggerBuilder
{
    private string _bindingName = "eventGridEvent";
    private string _id = Guid.NewGuid().ToString("N");
    private string _eventType = "test.event";
    private string _subject = "/tests/event";
    private string _dataVersion = "1.0";
    private DateTimeOffset _eventTime = DateTimeOffset.UtcNow;
    private object? _data;

    /// <summary>Sets the input binding name.</summary>
    /// <param name="bindingName">The non-empty worker input name. The default is <c>eventGridEvent</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventGridTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets the event identifier.</summary>
    /// <param name="id">The non-empty event identifier. The default is a newly generated GUID without separators.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventGridTriggerBuilder WithId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _id = id;
        return this;
    }

    /// <summary>Sets the event type.</summary>
    /// <param name="eventType">The non-empty event type. The default is <c>test.event</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventGridTriggerBuilder WithEventType(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        _eventType = eventType;
        return this;
    }

    /// <summary>Sets the event subject.</summary>
    /// <param name="subject">The non-empty subject. The default is <c>/tests/event</c>.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventGridTriggerBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the event occurrence time.</summary>
    /// <param name="eventTime">The absolute occurrence instant serialized into the event envelope.</param>
    /// <returns>This builder, for chaining.</returns>
    public EventGridTriggerBuilder At(DateTimeOffset eventTime)
    {
        _eventTime = eventTime;
        return this;
    }

    /// <summary>Sets the event data payload.</summary>
    /// <typeparam name="T">The payload type serialized when the trigger is built.</typeparam>
    /// <param name="data">The payload retained without cloning; null is accepted when <typeparamref name="T"/> permits it.</param>
    /// <returns>This builder, for chaining, replacing the prior payload.</returns>
    public EventGridTriggerBuilder WithData<T>(T data)
    {
        _data = data;
        return this;
    }

    /// <summary>Builds the Event Grid envelope as JSON text.</summary>
    /// <returns>
    /// New string trigger data containing the current event envelope, version <c>1.0</c>, and binding
    /// metadata for ID, event type, and subject.
    /// </returns>
    public TestTriggerData<string> Build()
    {
        var envelope = new
        {
            id = _id,
            eventType = _eventType,
            subject = _subject,
            eventTime = _eventTime,
            dataVersion = _dataVersion,
            data = _data,
        };
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = _id,
            ["EventType"] = _eventType,
            ["Subject"] = _subject,
        };
        return new(
            JsonSerializer.Serialize(envelope, JsonOptions.Default),
            _bindingName,
            "eventGridTrigger",
            metadata);
    }
}
