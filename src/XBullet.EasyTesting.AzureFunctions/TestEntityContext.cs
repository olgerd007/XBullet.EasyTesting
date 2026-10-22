using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Supplies entity identity and records outgoing signals and orchestration starts.</summary>
/// <remarks>Outgoing operations do not execute automatically. Inputs and options are retained by reference.</remarks>
public sealed class TestEntityContext : TaskEntityContext
{
    private readonly EntityRecording _recording = new();
    private readonly object _gate = new();
    private readonly List<RecordedOrchestrationStart> _starts = [];
    private long _sequence;

    /// <summary>Creates a context for one entity.</summary>
    /// <param name="id">The entity identity, including a non-empty name and non-null key.</param>
    public TestEntityContext(EntityInstanceId id)
    {
        EntityRecording.Validate(id);
        Id = id;
    }

    /// <inheritdoc />
    public override EntityInstanceId Id { get; }

    /// <summary>Gets outgoing signal attempts.</summary>
    /// <value>An ordered snapshot, including attempts whose handlers fail.</value>
    public IReadOnlyList<RecordedEntityOperation> Signals => _recording.Snapshot();

    /// <summary>Gets orchestration scheduling attempts.</summary>
    /// <value>An ordered snapshot; scheduling does not run orchestrations.</value>
    public IReadOnlyList<RecordedOrchestrationStart> ScheduledOrchestrations
    {
        get
        {
            lock (_gate)
            {
                return _starts.ToArray();
            }
        }
    }

    /// <summary>Gets or sets outgoing signal submission behavior.</summary>
    /// <value>An optional synchronous callback invoked after recording.</value>
    public Action<RecordedEntityOperation>? SignalHandler { get; set; }

    /// <summary>Gets or sets orchestration scheduling behavior.</summary>
    /// <value>An optional synchronous callback; null returns the explicit or generated test ID.</value>
    public Func<RecordedOrchestrationStart, string>? ScheduleHandler { get; set; }

    /// <inheritdoc />
    public override void SignalEntity(EntityInstanceId id, string operationName, object? input = null,
        SignalEntityOptions? options = null)
    {
        EntityRecording.Validate(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var call = _recording.Record(EntityOperationKind.Signal, id, operationName, input, options);
        SignalHandler?.Invoke(call);
    }

    /// <inheritdoc />
    public override string ScheduleNewOrchestration(TaskName name, object? input = null,
        StartOrchestrationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name.Name);
        if (options?.InstanceId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.InstanceId);
        }

        RecordedOrchestrationStart call;
        lock (_gate)
        {
            call = new(name, input, options, options?.InstanceId ?? $"test-entity-instance-{++_sequence}");
            _starts.Add(call);
        }

        var handler = ScheduleHandler;
        var result = handler is null ? call.InstanceId : handler(call);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        return result;
    }
}
