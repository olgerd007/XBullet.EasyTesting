using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Supplies ordered results and failures to explicit task handlers or connected activity delegates.</summary>
/// <typeparam name="TResult">The result type retained by reference.</typeparam>
/// <remarks>Concurrent calls reserve successive steps in invocation order. Exhaustion fails explicitly.
/// Reset rewinds consumption and preserves configured steps. Register separately as a scenario resource when needed.</remarks>
public sealed class TestDurableTaskSequence<TResult> : ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<(TResult? Result, Exception? Failure)> _steps = [];
    private int _consumed;

    /// <summary>Gets the number of consumed configured steps.</summary>
    /// <value>A count excluding canceled submissions and exhausted calls.</value>
    public int ConsumedCount
    {
        get
        {
            lock (_gate)
            {
                return _consumed;
            }
        }
    }

    /// <summary>Adds a successful outcome before consumption starts or after reset.</summary>
    /// <param name="result">The result retained without cloning.</param>
    /// <returns>This sequence for chaining.</returns>
    public TestDurableTaskSequence<TResult> ThenReturn(TResult result)
    {
        Add(result, null);
        return this;
    }

    /// <summary>Adds an exceptional outcome before consumption starts or after reset.</summary>
    /// <param name="exception">The non-null original exception returned by the next corresponding step.</param>
    /// <returns>This sequence for chaining.</returns>
    public TestDurableTaskSequence<TResult> ThenThrow(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(default, exception);
        return this;
    }

    /// <summary>Reserves the next configured outcome and returns its result or original failure.</summary>
    /// <param name="cancellationToken">Cancels before consuming a step.</param>
    /// <returns>A completed or faulted task; the sequence does not schedule work or perform retries itself.</returns>
    public Task<TResult> NextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (TResult? Result, Exception? Failure) step;
        lock (_gate)
        {
            if (_consumed == _steps.Count)
            {
                return Task.FromException<TResult>(new TestDurableTaskSequenceExhaustedException());
            }

            step = _steps[_consumed++];
        }

        return step.Failure is { } failure ? Task.FromException<TResult>(failure) : Task.FromResult(step.Result!);
    }

    /// <summary>Rewinds consumption while preserving configured outcomes.</summary>
    /// <param name="cancellationToken">Cancels before rewinding.</param>
    /// <returns>A completed task after reset.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _consumed = 0;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Captures configured, consumed, and remaining step counts.</summary>
    /// <param name="cancellationToken">Cancels before capture.</param>
    /// <returns>A detached snapshot excluding results and exception contents.</returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult<object?>(new { Configured = _steps.Count, Consumed = _consumed, Remaining = _steps.Count - _consumed });
        }
    }

    private void Add(TResult? result, Exception? failure)
    {
        lock (_gate)
        {
            if (_consumed != 0)
            {
                throw new InvalidOperationException("Configure sequence outcomes before consumption or after reset.");
            }

            _steps.Add((result, failure));
        }
    }
}

/// <summary>Indicates that an explicitly configured task outcome sequence has no remaining steps.</summary>
/// <remarks>Connected retry simulation treats this as a terminal test configuration failure.</remarks>
public sealed class TestDurableTaskSequenceExhaustedException : InvalidOperationException
{
    /// <summary>Creates an exhaustion failure without including step contents.</summary>
    public TestDurableTaskSequenceExhaustedException() : base("The Durable task outcome sequence is exhausted. Configure another step or reset the sequence.")
    {
    }
}
