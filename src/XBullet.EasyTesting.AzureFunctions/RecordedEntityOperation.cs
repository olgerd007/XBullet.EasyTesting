using Microsoft.DurableTask.Entities;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Identifies a recorded entity boundary operation.</summary>
public enum EntityOperationKind
{
    /// <summary>A request expecting an entity result.</summary>
    Call,
    /// <summary>A signal submission, without execution or delivery.</summary>
    Signal,
    /// <summary>An entity metadata lookup.</summary>
    Read,
    /// <summary>A pageable entity query.</summary>
    Query,
    /// <summary>An entity storage cleanup request.</summary>
    Clean,
    /// <summary>A local critical-section scope request.</summary>
    Lock
}

/// <summary>Describes an entity call attempt; inputs and options retain their original references.</summary>
/// <param name="Sequence">The one-based invocation sequence.</param>
/// <param name="Kind">The operation category.</param>
/// <param name="Id">The target entity, or null for queries, cleanup, and lock scopes.</param>
/// <param name="OperationName">The entity operation name, when applicable.</param>
/// <param name="Input">The original operation input.</param>
/// <param name="Options">The original options, read inclusion flag, query, cleanup request, or lock IDs.</param>
/// <param name="ResultType">The requested result type, or null when there is no result.</param>
public sealed record RecordedEntityOperation(long Sequence, EntityOperationKind Kind, EntityInstanceId? Id,
    string? OperationName, object? Input, object? Options, Type? ResultType);

internal sealed class EntityRecording
{
    private readonly object _gate = new();
    private readonly List<RecordedEntityOperation> _operations = [];
    private long _sequence;

    public IReadOnlyList<RecordedEntityOperation> Snapshot()
    {
        lock (_gate)
        {
            return _operations.ToArray();
        }
    }

    public RecordedEntityOperation Record(EntityOperationKind kind, EntityInstanceId? id = null,
        string? name = null, object? input = null, object? options = null, Type? resultType = null)
    {
        lock (_gate)
        {
            var call = new RecordedEntityOperation(++_sequence, kind, id, name, input, options, resultType);
            _operations.Add(call);
            return call;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _operations.Clear();
            _sequence = 0;
        }
    }

    public object Diagnostics() => Snapshot().Select(call => new
    {
        call.Sequence,
        Kind = call.Kind.ToString(),
        EntityName = call.Id?.Name,
        EntityKey = call.Id?.Key,
        call.OperationName
    }).ToArray();

    public static void Validate(EntityInstanceId id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Name);
        ArgumentNullException.ThrowIfNull(id.Key);
    }

    public static T ConvertResult<T>(object? result)
    {
        if (result is T typed)
        {
            return typed;
        }

        if (result is null && default(T) is null)
        {
            return default!;
        }

        throw new InvalidOperationException($"The entity handler did not return a value assignable to {typeof(T)}.");
    }
}
