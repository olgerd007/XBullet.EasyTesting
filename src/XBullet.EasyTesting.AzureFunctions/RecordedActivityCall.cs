using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Describes one activity scheduled by a test orchestration context.</summary>
/// <param name="ActivityName">The Durable Task activity name.</param>
/// <param name="Input">The input retained without cloning, or <see langword="null"/> when none was supplied.</param>
/// <param name="Options">The task options retained without cloning, or <see langword="null"/> when omitted.</param>
/// <param name="ResultType">The non-null result type requested by the orchestration.</param>
public sealed record RecordedActivityCall(
    string ActivityName,
    object? Input,
    TaskOptions? Options,
    Type ResultType);
