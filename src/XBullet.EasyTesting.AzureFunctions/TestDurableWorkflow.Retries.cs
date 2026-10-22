using Microsoft.DurableTask;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableWorkflow
{
    private readonly List<RecordedDurableTaskAttempt> _attempts = [];
    private long _attemptSequence;
    private long _taskCallSequence;
    private int? _maximumAttempts;
    private Action<RecordedDurableTaskAttempt>? _retryScheduled;

    /// <summary>Gets immutable snapshots of activity and child attempts, including calls without retry options.</summary>
    /// <value>An ordered snapshot containing original exceptions; diagnostics omit exception contents.</value>
    public IReadOnlyList<RecordedDurableTaskAttempt> Attempts
    {
        get
        {
            lock (_gate)
            {
                return _attempts.ToArray();
            }
        }
    }

    /// <summary>Enables bounded simulation of SDK retry policies and handlers for connected task calls.</summary>
    /// <param name="maximumAttempts">The positive upper bound per logical call, including the first attempt. Defaults to 100.</param>
    /// <param name="retryScheduled">An optional observer called outside workflow locks after accepting a retry. It may advance the caller's manual clock. Observer failures stop the call.</param>
    /// <returns>This workflow for chaining. Configure before scheduling or after successful reset.</returns>
    /// <remarks>Only calls with retry options retry. Policy delays use the calling context's manual clock;
    /// custom handlers control their own delay. Cancellation and sequence exhaustion are terminal.
    /// Reset preserves configuration and clears attempts. This does not emulate Durable replay or persisted retries.</remarks>
    public TestDurableWorkflow WithRetries(int maximumAttempts = 100, Action<RecordedDurableTaskAttempt>? retryScheduled = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        lock (_gate)
        {
            EnsureRegistrationAvailable();
            _maximumAttempts = maximumAttempts;
            _retryScheduled = retryScheduled;
        }

        return this;
    }

    private async Task<object?> ExecuteWithRetriesAsync(WorkflowInstance caller, DurableTaskAttemptKind kind, string name,
        TaskOptions? options, Func<int, Task<object?>> invoke, Func<int, string?>? childId = null)
    {
        long callId;
        lock (_gate)
        {
            callId = ++_taskCallSequence;
        }

        var started = caller.Context.CurrentUtcDateTime;
        var policy = options?.Retry?.Policy;
        for (var number = 1; ; number++)
        {
            caller.Token.ThrowIfCancellationRequested();
            RecordedDurableTaskAttempt attempt;
            lock (_gate)
            {
                attempt = new(++_attemptSequence, callId, caller.Context.InstanceId, name, kind, number,
                    childId?.Invoke(number), caller.Context.CurrentUtcDateTime, null, DurableTaskAttemptStatus.Running, null, null);
                _attempts.Add(attempt);
            }

            try
            {
                var result = await invoke(number).ConfigureAwait(false);
                UpdateAttempt(attempt with { CompletedAt = caller.Context.CurrentUtcDateTime, Status = DurableTaskAttemptStatus.Succeeded });
                return result;
            }
            catch (Exception exception)
            {
                attempt = attempt with
                {
                    CompletedAt = caller.Context.CurrentUtcDateTime,
                    Status = exception is OperationCanceledException ? DurableTaskAttemptStatus.Canceled : DurableTaskAttemptStatus.Failed,
                    Failure = exception
                };
                UpdateAttempt(attempt);
                caller.Token.ThrowIfCancellationRequested();
                if (_maximumAttempts is not { } limit || number >= limit || options?.Retry is not { } retry
                    || exception is OperationCanceledException or TestDurableTaskSequenceExhaustedException)
                {
                    throw;
                }

                var elapsed = caller.Context.CurrentUtcDateTime - started;
                TimeSpan delay;
                if (policy is not null)
                {
                    if (number >= policy.MaxNumberOfAttempts || TimedOut(policy, elapsed))
                    {
                        throw;
                    }

                    delay = RetryDelay(policy, number);
                    if (policy.RetryTimeout != Timeout.InfiniteTimeSpan && delay >= policy.RetryTimeout - elapsed)
                    {
                        throw;
                    }

                    // SDK HandleFailure adapts through DurableTask.Core exceptions, not arbitrary CLR exceptions.
                    var bridge = new DurableTask.Core.Exceptions.TaskFailedException(exception.Message, exception)
                    {
                        FailureDetails = ToCoreFailure(FailureDetails(exception))
                    };
                    if (!policy.Handle(bridge))
                    {
                        throw;
                    }
                }
                else if (retry.Handler is { } handler)
                {
                    if (!await handler(new Microsoft.DurableTask.RetryContext(caller.Context, number,
                        FailureDetails(exception), elapsed, caller.Token)).ConfigureAwait(false))
                    {
                        throw;
                    }

                    delay = TimeSpan.Zero;
                }
                else
                {
                    throw;
                }

                caller.Token.ThrowIfCancellationRequested();
                var now = caller.Context.CurrentUtcDateTime;
                if (delay.Ticks > DateTime.MaxValue.Ticks - now.Ticks)
                {
                    throw new InvalidOperationException("The retry delay exceeds the supported manual clock range.");
                }

                var due = now.Add(delay);
                attempt = attempt with { RetryAt = due };
                UpdateAttempt(attempt);
                _retryScheduled?.Invoke(attempt);
                await caller.Context.CreateTimer(due, caller.Token).ConfigureAwait(false);
                if (policy is not null && TimedOut(policy, caller.Context.CurrentUtcDateTime - started))
                {
                    throw;
                }
            }
        }
    }

    private void UpdateAttempt(RecordedDurableTaskAttempt attempt)
    {
        lock (_gate)
        {
            _attempts[checked((int)attempt.Sequence - 1)] = attempt;
        }
    }

    private static bool TimedOut(RetryPolicy policy, TimeSpan elapsed) =>
        policy.RetryTimeout != Timeout.InfiniteTimeSpan && elapsed >= policy.RetryTimeout;

    private static TimeSpan RetryDelay(RetryPolicy policy, int failedAttempt)
    {
        var ticks = policy.FirstRetryInterval.Ticks * Math.Pow(policy.BackoffCoefficient, failedAttempt - 1);
        if (double.IsNaN(ticks))
        {
            throw new InvalidOperationException("The retry policy has an invalid backoff coefficient.");
        }

        var maximum = policy.MaxRetryInterval == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue.Ticks : policy.MaxRetryInterval.Ticks;
        return TimeSpan.FromTicks(ticks >= maximum ? maximum : (long)ticks);
    }

    private static TaskFailureDetails FailureDetails(Exception exception) =>
        exception is TaskFailedException failed ? failed.FailureDetails : TaskFailureDetails.FromException(exception);

    private static DurableTask.Core.FailureDetails ToCoreFailure(TaskFailureDetails failure) =>
        new(failure.ErrorType, failure.ErrorMessage, failure.StackTrace,
            failure.InnerFailure is { } inner ? ToCoreFailure(inner) : null, false, failure.Properties);
}
