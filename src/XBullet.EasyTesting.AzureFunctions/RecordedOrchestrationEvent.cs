namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Describes an event sent by a test orchestration to an instance.</summary>
/// <param name="InstanceId">The non-empty target orchestration instance ID.</param>
/// <param name="EventName">The non-empty event name.</param>
/// <param name="Payload">The payload retained without cloning.</param>
public sealed record RecordedOrchestrationEvent(string InstanceId, string EventName, object? Payload);
