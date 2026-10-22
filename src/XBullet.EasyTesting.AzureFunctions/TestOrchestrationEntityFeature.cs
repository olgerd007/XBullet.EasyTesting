using Microsoft.DurableTask.Entities;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Records orchestrator entity interactions and supplies explicitly configured call results.</summary>
/// <remarks>Attach with <see cref="TestOrchestrationContext.WithEntities"/>. Signals record submissions only.
/// Lock scopes mark local critical sections; they do not simulate distributed mutual exclusion.
/// Configure handlers before use and await all operations before resetting.</remarks>
public sealed class TestOrchestrationEntityFeature : TaskOrchestrationEntityFeature, ITestScenarioResource
{
    private readonly EntityRecording _recording = new();
    private readonly object _gate = new();
    private readonly List<IReadOnlyList<EntityInstanceId>> _scopes = [];

    /// <summary>Gets entity call attempts in invocation order.</summary>
    /// <value>A detached list retaining original input and options references.</value>
    public IReadOnlyList<RecordedEntityOperation> Operations => _recording.Snapshot();

    /// <summary>Gets or sets entity call behavior.</summary>
    /// <value>A required callback for calls, including void calls. Typed results use assignment compatibility.</value>
    public Func<RecordedEntityOperation, Task<object?>>? CallHandler { get; set; }

    /// <summary>Gets or sets signal submission behavior.</summary>
    /// <value>An optional callback; null accepts and records the submission without delivery.</value>
    public Func<RecordedEntityOperation, Task>? SignalHandler { get; set; }

    /// <inheritdoc />
    public override async Task<TResult> CallEntityAsync<TResult>(EntityInstanceId id, string operationName,
        object? input = null, CallEntityOptions? options = null)
    {
        var result = await CallAsync(id, operationName, input, options, typeof(TResult)).ConfigureAwait(false);
        return EntityRecording.ConvertResult<TResult>(result);
    }

    /// <inheritdoc />
    public override async Task CallEntityAsync(EntityInstanceId id, string operationName,
        object? input = null, CallEntityOptions? options = null) =>
        _ = await CallAsync(id, operationName, input, options, null).ConfigureAwait(false);

    private Task<object?> CallAsync(EntityInstanceId id, string operationName, object? input,
        CallEntityOptions? options, Type? resultType)
    {
        EntityRecording.Validate(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var call = _recording.Record(EntityOperationKind.Call, id, operationName, input, options, resultType);
        return (CallHandler ?? throw new InvalidOperationException("Configure TestOrchestrationEntityFeature.CallHandler before calling an entity."))(call);
    }

    /// <inheritdoc />
    public override Task SignalEntityAsync(EntityInstanceId id, string operationName,
        object? input = null, SignalEntityOptions? options = null)
    {
        EntityRecording.Validate(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var call = _recording.Record(EntityOperationKind.Signal, id, operationName, input, options);
        return SignalHandler?.Invoke(call) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        var ids = entityIds.Distinct().ToArray();
        foreach (var id in ids)
        {
            EntityRecording.Validate(id);
        }

        IReadOnlyList<EntityInstanceId> scope = Array.AsReadOnly(ids);
        _recording.Record(EntityOperationKind.Lock, options: scope);
        lock (_gate)
        {
            _scopes.Add(scope);
        }

        return Task.FromResult<IAsyncDisposable>(new EntityScope(() =>
        {
            lock (_gate)
            {
                _scopes.Remove(scope);
            }
        }));
    }

    /// <inheritdoc />
    public override bool InCriticalSection(out IReadOnlyList<EntityInstanceId> entityIds)
    {
        lock (_gate)
        {
            entityIds = Array.AsReadOnly(_scopes.SelectMany(scope => scope).Distinct().ToArray());
            return entityIds.Count > 0;
        }
    }

    /// <summary>Clears recordings and active local scope markers, retaining configured handlers.</summary>
    /// <param name="cancellationToken">Cancels before any state changes.</param>
    /// <returns>A completed task after reset.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _scopes.Clear();
            _recording.Reset();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Captures entity IDs, operation names, kinds, and sequence numbers.</summary>
    /// <param name="cancellationToken">Cancels before capture.</param>
    /// <returns>A detached snapshot excluding inputs, results, options, and failures.</returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<object?>(_recording.Diagnostics());
    }

    private sealed class EntityScope(Action release) : IAsyncDisposable
    {
        private Action? _release = release;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
