namespace XBullet.EasyTesting.AzureFunctions;

internal sealed class TestOrchestrationScheduler
{
    private readonly object _gate = new();
    private readonly List<(DateTime FireAt, PendingWait Wait)> _timers = [];
    private readonly Dictionary<string, Queue<object?>> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkedList<PendingWait>> _eventWaiters = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _utcNow = DateTime.UnixEpoch;

    public DateTime UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    public void AdvanceBy(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        lock (_gate)
        {
            AdvanceTo(_utcNow.Add(duration));
        }
    }

    public void AdvanceTo(DateTime utcDateTime)
    {
        ValidateUtc(utcDateTime);
        lock (_gate)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(utcDateTime, _utcNow);
            _utcNow = utcDateTime;
            var due = _timers.Where(timer => timer.FireAt <= _utcNow)
                .OrderBy(timer => timer.FireAt)
                .ToArray();
            foreach (var timer in due)
            {
                _timers.Remove(timer);
                Complete(timer.Wait, null);
            }
        }
    }

    public Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        ValidateUtc(fireAt);
        PendingWait pending;
        lock (_gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (fireAt <= _utcNow)
            {
                return Task.CompletedTask;
            }

            pending = new PendingWait(EventName: null);
            _timers.Add((fireAt, pending));
        }

        return WaitAsync(pending, cancellationToken);
    }

    public Task<object?> WaitForEvent(string eventName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        PendingWait pending;
        lock (_gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<object?>(cancellationToken);
            }

            if (_events.TryGetValue(eventName, out var buffered))
            {
                var value = buffered.Dequeue();
                if (buffered.Count == 0)
                {
                    _events.Remove(eventName);
                }

                return Task.FromResult(value);
            }

            pending = new PendingWait(eventName);
            if (!_eventWaiters.TryGetValue(eventName, out var waiters))
            {
                waiters = new LinkedList<PendingWait>();
                _eventWaiters.Add(eventName, waiters);
            }

            waiters.AddLast(pending);
        }

        return WaitAsync(pending, cancellationToken);
    }

    public void RaiseEvent(string eventName, object? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        lock (_gate)
        {
            if (_eventWaiters.TryGetValue(eventName, out var waiters))
            {
                var pending = waiters.First!.Value;
                waiters.RemoveFirst();
                if (waiters.Count == 0)
                {
                    _eventWaiters.Remove(eventName);
                }

                Complete(pending, payload);
                return;
            }

            if (!_events.TryGetValue(eventName, out var buffered))
            {
                buffered = new Queue<object?>();
                _events.Add(eventName, buffered);
            }

            buffered.Enqueue(payload);
        }
    }

    private Task<object?> WaitAsync(PendingWait pending, CancellationToken cancellationToken)
    {
        var registration = cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (pending.EventName is null)
                {
                    _timers.RemoveAll(timer => ReferenceEquals(timer.Wait, pending));
                }
                else if (_eventWaiters.TryGetValue(pending.EventName, out var waiters))
                {
                    waiters.Remove(pending);
                    if (waiters.Count == 0)
                    {
                        _eventWaiters.Remove(pending.EventName);
                    }
                }

                pending.Completion.TrySetCanceled(cancellationToken);
                pending.Registration.Unregister();
            }
        });

        lock (_gate)
        {
            if (!pending.Completion.Task.IsCompleted)
            {
                pending.Registration = registration;
                return pending.Completion.Task;
            }
        }

        // Completion can win before registration is assigned. Dispose outside the scheduler lock.
        registration.Dispose();
        return pending.Completion.Task;
    }

    private static void Complete(PendingWait pending, object? value)
    {
        pending.Completion.TrySetResult(value);
        // Unregister never waits for a cancellation callback that may be acquiring the scheduler lock.
        pending.Registration.Unregister();
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Orchestration time must have DateTimeKind.Utc.", nameof(value));
        }
    }

    private sealed record PendingWait(string? EventName)
    {
        public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Registration { get; set; }
    }
}
