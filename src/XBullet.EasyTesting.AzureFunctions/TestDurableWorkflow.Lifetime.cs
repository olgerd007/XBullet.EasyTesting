namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableWorkflow
{
    /// <summary>Cancels and drains all executions, clears client recordings and instances, and retains registrations.</summary>
    /// <param name="cancellationToken">Cancels cleanup waiting. Once cleanup begins, execution cancellation is not rolled back.</param>
    /// <returns>A value task completing when invocation scopes close and state is reset.</returns>
    /// <remarks>Cleanup is bounded by the constructor timeout. Failed cleanup retains instance diagnostics for inspection and retry. Concurrent cleanup is rejected.</remarks>
    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkflowInstance[] instances;
        Task[] activities;
        lock (_gate)
        {
            EnsureAvailable();
            _resetting = true;
            instances = _instances.Values.ToArray();
            activities = _activityExecutions.Concat(_childExecutions).ToArray();
        }

        try
        {
            await StopInstancesAsync(instances, activities, cancellationToken).ConfigureAwait(false);
            await Client.ResetAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var instance in instances)
                {
                    instance.Cancellation.Dispose();
                }

                _instances.Clear();
                _childSequence = 0;
                _attempts.Clear();
                _attemptSequence = 0;
                _taskCallSequence = 0;
                _registrationsFrozen = false;
            }
        }
        finally
        {
            lock (_gate)
            {
                _resetting = false;
            }
        }
    }

    /// <summary>Captures client operations and workflow summaries before scenario cleanup.</summary>
    /// <param name="cancellationToken">Cancels before the snapshot is captured.</param>
    /// <returns>A serializable snapshot containing instance IDs, names, statuses, parent IDs, and call counts; payloads and failures are excluded.</returns>
    public async ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = await Client.CaptureDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            return new
            {
                Client = client,
                SerializationEnabled = _dataConverter is not null,
                RetrySimulationEnabled = _maximumAttempts is not null,
                ActiveActivities = _activityExecutions.Count,
                ActiveChildCalls = _childExecutions.Count,
                Attempts = _attempts.Select(attempt => new
                {
                    attempt.Sequence,
                    attempt.CallId,
                    attempt.InstanceId,
                    attempt.TaskName,
                    Kind = attempt.Kind.ToString(),
                    attempt.AttemptNumber,
                    attempt.ChildInstanceId,
                    attempt.StartedAt,
                    attempt.CompletedAt,
                    Status = attempt.Status.ToString(),
                    attempt.RetryAt
                }).ToArray(),
                Instances = _instances.Values.OrderBy(instance => instance.Context.InstanceId, StringComparer.Ordinal)
                    .Select(instance => new
                    {
                        InstanceId = instance.Context.InstanceId,
                        Name = instance.Name.Name,
                        Status = instance.Status.ToString(),
                        ParentInstanceId = instance.Context.Parent?.InstanceId,
                        ActivityCalls = instance.Context.ActivityCalls.Count,
                        ChildCalls = instance.Context.SubOrchestrationCalls.Count,
                        SentEvents = instance.Context.SentEvents.Count
                    }).ToArray()
            };
        }
    }

    /// <summary>Cancels and drains executions before releasing workflow-owned cancellation resources.</summary>
    /// <returns>A value task completing within the configured cleanup timeout. The host is never disposed by this workflow.</returns>
    /// <remarks>On timeout, keep the host alive, release uncooperative work, and retry disposal. Client recordings remain available.</remarks>
    public async ValueTask DisposeAsync()
    {
        WorkflowInstance[] instances;
        Task[] activities;
        lock (_gate)
        {
            if (_disposed && _instances.Count == 0)
            {
                return;
            }

            if (_resetting)
            {
                throw new InvalidOperationException("Connected workflow cleanup is already in progress.");
            }

            _disposed = true;
            _resetting = true;
            instances = _instances.Values.ToArray();
            activities = _activityExecutions.Concat(_childExecutions).ToArray();
        }

        try
        {
            await StopInstancesAsync(instances, activities, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var instance in instances)
                {
                    instance.Cancellation.Dispose();
                }

                _instances.Clear();
            }

            if (_ownsClient)
            {
                await Client.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _resetting = false;
            }
        }
    }

    private async Task StopInstancesAsync(WorkflowInstance[] instances, Task[] activities, CancellationToken cancellationToken)
    {
        var stopping = instances.Select(instance => instance.Cancellation.CancelAsync())
            .Concat(instances.Select(instance => instance.Completed.Task)).Concat(activities);
        await Task.WhenAll(stopping).WaitAsync(_shutdownTimeout, cancellationToken).ConfigureAwait(false);
    }
}
