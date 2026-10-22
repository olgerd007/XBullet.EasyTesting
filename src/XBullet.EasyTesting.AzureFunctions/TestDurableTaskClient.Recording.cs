using System.Collections.ObjectModel;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableTaskClient
{
    private readonly List<RecordedDurableClientOperation> _operations = [];
    private readonly Dictionary<string, OrchestrationMetadata> _instances = new(StringComparer.Ordinal);
    private long _operationSequence;

    /// <summary>Gets all client call attempts, including failed handlers and missing-handler errors.</summary>
    /// <value>An ordered snapshot. Invalid arguments and pre-canceled calls are excluded; argument values are retained by reference.</value>
    public IReadOnlyList<RecordedDurableClientOperation> Operations
    {
        get
        {
            lock (_gate)
            {
                return _operations.ToArray();
            }
        }
    }

    /// <summary>Seeds or replaces metadata used by default instance lookups.</summary>
    /// <param name="metadata">Non-null metadata with non-empty name and ID. Scalar values and tags are copied; converter and failure details are retained.</param>
    /// <returns>This client for chaining. A configured lookup handler takes precedence.</returns>
    /// <remarks>No scheduling or management operation changes these fixtures. Seed inside scenarios after their initial reset.</remarks>
    public TestDurableTaskClient WithInstance(OrchestrationMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.InstanceId);
        var copy = TestOrchestrationMetadataBuilder.Copy(metadata, true);
        lock (_gate)
        {
            _instances[copy.InstanceId] = copy;
        }

        return this;
    }

    /// <summary>Starts fluent assertions over recorded attempts.</summary>
    /// <returns>A new assertion object that reads current recording snapshots.</returns>
    public TestDurableTaskClientAssertions Should() => new(this);

    /// <summary>Clears recorded calls, seeded orchestration and entity metadata, and generated ID sequences.</summary>
    /// <param name="cancellationToken">Cancels before state changes. Handlers remain configured.</param>
    /// <returns>A completed value task once state is cleared.</returns>
    /// <remarks>Await all in-flight calls before resetting; reset does not cancel handlers or their side effects.</remarks>
    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Entities.ResetAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _starts.Clear();
            _operations.Clear();
            _instances.Clear();
            _sequence = 0;
            _operationSequence = 0;
        }
    }

    /// <summary>Captures a serializable operation timeline and seeded instance summaries.</summary>
    /// <param name="cancellationToken">Cancels before capture begins.</param>
    /// <returns>A detached snapshot containing names, IDs, operation targets, and statuses; payloads, reasons, options, and failures are excluded.</returns>
    public async ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entityDiagnostics = await Entities.CaptureDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            return new
            {
                ClientName = Name,
                Entities = entityDiagnostics,
                Operations = _operations.Select(call => new
                {
                    call.Sequence,
                    Operation = call.Operation.ToString(),
                    call.InstanceId,
                    Target = Target(call)
                }).ToArray(),
                Instances = _instances.Values.OrderBy(instance => instance.InstanceId, StringComparer.Ordinal)
                    .Select(instance => new { instance.Name, instance.InstanceId, RuntimeStatus = instance.RuntimeStatus.ToString() }).ToArray()
            };
        }
    }

    private void Record(DurableClientOperation operation, string? instanceId, params (string Name, object? Value)[] arguments)
    {
        var values = new ReadOnlyDictionary<string, object?>(arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal));
        lock (_gate)
        {
            _operations.Add(new(++_operationSequence, operation, instanceId, values));
        }
    }

    internal static string? Target(RecordedDurableClientOperation call) =>
        call.Operation switch
        {
            DurableClientOperation.Schedule => call.GetArgument<TaskName>("orchestratorName").Name,
            DurableClientOperation.RaiseEvent => call.GetArgument<string>("eventName"),
            _ => null
        };
}
