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
    public EventGridTriggerBuilder Named(string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        _bindingName = bindingName;
        return this;
    }

    /// <summary>Sets the event identifier.</summary>
    public EventGridTriggerBuilder WithId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _id = id;
        return this;
    }

    /// <summary>Sets the event type.</summary>
    public EventGridTriggerBuilder WithEventType(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        _eventType = eventType;
        return this;
    }

    /// <summary>Sets the event subject.</summary>
    public EventGridTriggerBuilder WithSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the event occurrence time.</summary>
    public EventGridTriggerBuilder At(DateTimeOffset eventTime)
    {
        _eventTime = eventTime;
        return this;
    }

    /// <summary>Sets the event data payload.</summary>
    public EventGridTriggerBuilder WithData<T>(T data)
    {
        _data = data;
        return this;
    }

    /// <summary>Builds the Event Grid envelope as JSON text.</summary>
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
