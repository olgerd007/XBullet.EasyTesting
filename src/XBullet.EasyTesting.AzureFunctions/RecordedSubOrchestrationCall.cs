using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Describes one sub-orchestrator scheduled by a test orchestration context.</summary>
/// <param name="OrchestratorName">The Durable Task orchestrator name.</param>
/// <param name="Input">The input retained without cloning, or <see langword="null"/> when omitted.</param>
/// <param name="Options">The task options, including any child instance ID, retained without cloning.</param>
/// <param name="ResultType">The non-null result type requested by the parent orchestration.</param>
public sealed record RecordedSubOrchestrationCall(
    string OrchestratorName,
    object? Input,
    TaskOptions? Options,
    Type ResultType);
