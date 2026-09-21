using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Describes one activity scheduled by a test orchestration context.</summary>
public sealed record RecordedActivityCall(
    string ActivityName,
    object? Input,
    TaskOptions? Options,
    Type ResultType);
