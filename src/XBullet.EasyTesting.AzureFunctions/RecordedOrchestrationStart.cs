using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Records an attempt to schedule an orchestration without executing it.</summary>
/// <param name="OrchestratorName">The requested task name, including its version.</param>
/// <param name="Input">The original input reference, without serialization or cloning.</param>
/// <param name="Options">The original options reference, or null when omitted.</param>
/// <param name="InstanceId">The explicit instance ID or a generated test instance ID.</param>
public sealed record RecordedOrchestrationStart(
    TaskName OrchestratorName,
    object? Input,
    StartOrchestrationOptions? Options,
    string InstanceId);
