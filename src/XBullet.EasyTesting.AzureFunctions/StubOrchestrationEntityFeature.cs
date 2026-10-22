using Microsoft.DurableTask.Entities;

namespace XBullet.EasyTesting.AzureFunctions;

internal sealed class StubOrchestrationEntityFeature : TaskOrchestrationEntityFeature
{
    private readonly object _gate = new();
    private readonly List<IReadOnlyList<EntityInstanceId>> _scopes = [];

    public override Task<TResult> CallEntityAsync<TResult>(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        CallEntityOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        return Task.FromResult(default(TResult)!);
    }

    public override Task CallEntityAsync(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        CallEntityOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        return Task.CompletedTask;
    }

    public override Task SignalEntityAsync(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        SignalEntityOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        return Task.CompletedTask;
    }

    public override Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        IReadOnlyList<EntityInstanceId> scope = Array.AsReadOnly(entityIds.Distinct().ToArray());
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

    public override bool InCriticalSection(out IReadOnlyList<EntityInstanceId> entityIds)
    {
        lock (_gate)
        {
            entityIds = Array.AsReadOnly(_scopes.SelectMany(scope => scope).Distinct().ToArray());
            return entityIds.Count > 0;
        }
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
