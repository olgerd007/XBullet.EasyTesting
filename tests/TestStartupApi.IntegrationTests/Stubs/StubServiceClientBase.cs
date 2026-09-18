using XBullet.EasyTesting.Hosting;

namespace TestStartupApi.IntegrationTests.Stubs;

public abstract class StubServiceClientBase<TOperation> : ITestScenarioResource
    where TOperation : struct, Enum
{
    private readonly object _gate = new();
    private readonly Dictionary<ResponseKey, ArrangedResponse> _responses = [];
    private readonly List<StubClientCall<TOperation>> _calls = [];

    public IReadOnlyList<StubClientCall<TOperation>> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _calls.Count;
            }
        }
    }

    public StubServiceClientBase<TOperation> VerifyCalled(
        TOperation operation,
        string? resourceId = null,
        int expectedCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);

        int actualCount;
        lock (_gate)
        {
            actualCount = _calls.Count(call =>
                EqualityComparer<TOperation>.Default.Equals(call.Operation, operation) &&
                (resourceId is null || string.Equals(
                    call.ResourceId,
                    resourceId,
                    StringComparison.Ordinal)));
        }

        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {operation} '{resourceId ?? "*"}' to be called " +
                $"{expectedCount} time(s), but it was called {actualCount} time(s).");
        }

        return this;
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _responses.Clear();
            _calls.Clear();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<object?> CaptureDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult<object?>(new
            {
                StubType = GetType().FullName,
                Calls = _calls.ToArray(),
                Arrangements = _responses.Keys
                    .Select(key => new { key.Operation, key.ResourceId })
                    .ToArray()
            });
        }
    }

    protected void ArrangeResponse<TResponse>(
        TOperation operation,
        TResponse response,
        string? resourceId = null)
    {
        lock (_gate)
        {
            _responses[new ResponseKey(operation, resourceId)] = new ArrangedResponse(response);
        }
    }

    protected Task<TResponse> InvokeAsync<TResponse>(
        TOperation operation,
        string? resourceId = null,
        object? request = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _calls.Add(new StubClientCall<TOperation>(operation, resourceId, request));
            var key = new ResponseKey(operation, resourceId);
            if (!_responses.TryGetValue(key, out var arranged))
            {
                throw new InvalidOperationException(
                    $"No response was arranged for {GetType().Name}.{operation}" +
                    (resourceId is null ? "." : $" '{resourceId}'."));
            }

            if (arranged.Value is null)
            {
                return Task.FromResult(default(TResponse)!);
            }

            if (arranged.Value is not TResponse response)
            {
                throw new InvalidOperationException(
                    $"The response arranged for {GetType().Name}.{operation} has type " +
                    $"'{arranged.Value.GetType().Name}', not '{typeof(TResponse).Name}'.");
            }

            return Task.FromResult(response);
        }
    }

    private readonly record struct ResponseKey(TOperation Operation, string? ResourceId);

    private sealed record ArrangedResponse(object? Value);
}

public sealed record StubClientCall<TOperation>(
    TOperation Operation,
    string? ResourceId,
    object? Request)
    where TOperation : struct, Enum;
