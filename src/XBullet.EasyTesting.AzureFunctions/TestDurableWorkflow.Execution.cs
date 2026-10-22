using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableWorkflow
{
    private WorkflowInstance Start(TaskName name, string id, object? input, string? version, WorkflowInstance? parent, CancellationToken schedulingToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        schedulingToken.ThrowIfCancellationRequested();
        var capturedInput = CapturePayload(input);
        WorkflowInstance instance;
        Func<WorkflowInstance, Task<object?>> handler;
        lock (_gate)
        {
            EnsureAvailable();
            _ = Host;
            schedulingToken.ThrowIfCancellationRequested();
            if (!_orchestrators.TryGetValue(name.Name, out handler!))
            {
                throw new InvalidOperationException($"No connected orchestrator named '{name.Name}' is registered.");
            }

            if (_instances.ContainsKey(id))
            {
                throw new InvalidOperationException($"Connected workflow instance '{id}' already exists. Reset before reusing its ID.");
            }

            var cancellation = parent is null ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(parent.Token);
            var context = new TestOrchestrationContext { PayloadConverter = _dataConverter }
                .WithName(name).WithInstanceId(id).WithInput(capturedInput)
                .WithVersion(version ?? string.Empty);
            if (parent is not null)
            {
                context.WithParent(new ParentOrchestrationInstance(parent.Name, parent.Context.InstanceId))
                    .AdvanceTimeTo(parent.Context.CurrentUtcDateTime);
            }

            instance = new(name, capturedInput, context, cancellation);
            context.ExecutionCancellation = cancellation.Token;
            context.ActivityHandler = call => DispatchActivityAsync(instance, call);
            context.SubOrchestratorHandler = call => DispatchChildAsync(instance, call);
            context.EventDispatcher = call =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                RouteEvent(call.InstanceId, call.EventName, call.Payload);
            };
            context.WithContinueAsNewHandler(_ => throw new InvalidOperationException(
                "Connected workflows do not restart continuations. Test ContinueAsNew explicitly with separate orchestration contexts."));
            _instances.Add(id, instance);
            _registrationsFrozen = true;
        }

        // Execution is tracked through Completed; ExecuteAsync records every failure instead of leaving a faulted background task.
        _ = ExecuteAsync(instance, handler);
        return instance;
    }

    private async Task ExecuteAsync(WorkflowInstance instance, Func<WorkflowInstance, Task<object?>> handler)
    {
        await Task.Yield();
        try
        {
            instance.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                instance.Status = OrchestrationRuntimeStatus.Running;
            }

            instance.Started.TrySetResult();
            var output = await handler(instance).ConfigureAwait(false);
            instance.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                instance.Output = output;
                instance.Status = OrchestrationRuntimeStatus.Completed;
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                instance.Failure = exception;
                instance.Status = instance.Cancellation.IsCancellationRequested
                    ? OrchestrationRuntimeStatus.Terminated : OrchestrationRuntimeStatus.Failed;
            }
        }
        finally
        {
            instance.Started.TrySetResult();
            instance.Completed.TrySetResult();
        }
    }

    private Task<object?> DispatchActivityAsync(WorkflowInstance instance, RecordedActivityCall call)
    {
        instance.Token.ThrowIfCancellationRequested();
        Func<object?, CancellationToken, Task<object?>> handler;
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            EnsureAvailable();
            if (!_activities.TryGetValue(call.ActivityName, out handler!))
            {
                throw new InvalidOperationException($"No connected activity named '{call.ActivityName}' is registered.");
            }

            _activityExecutions.Add(drained.Task);
        }

        return ExecuteActivityAsync(instance, call, handler, drained);
    }

    private async Task<object?> ExecuteActivityAsync(WorkflowInstance instance, RecordedActivityCall call,
        Func<object?, CancellationToken, Task<object?>> handler, TaskCompletionSource drained)
    {
        try
        {
            var input = CapturePayload(call.Input);
            return await ExecuteWithRetriesAsync(instance, DurableTaskAttemptKind.Activity, call.ActivityName, call.Options,
                _ => handler(input, instance.Token)).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _activityExecutions.Remove(drained.Task);
            }

            drained.TrySetResult();
        }
    }

    private async Task<object?> DispatchChildAsync(WorkflowInstance parent, RecordedSubOrchestrationCall call)
    {
        parent.Token.ThrowIfCancellationRequested();
        var options = call.Options as SubOrchestrationOptions;
        string childId;
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            EnsureAvailable();
            if (!_orchestrators.ContainsKey(call.OrchestratorName))
            {
                throw new InvalidOperationException($"No connected orchestrator named '{call.OrchestratorName}' is registered.");
            }

            childId = options?.InstanceId ?? $"{parent.Context.InstanceId}:child:{++_childSequence}";
            _childExecutions.Add(drained.Task);
        }

        try
        {
            var input = CapturePayload(call.Input);
            string AttemptId(int number) => number == 1 ? childId : $"{childId}:retry:{number}";
            return await ExecuteWithRetriesAsync(parent, DurableTaskAttemptKind.SubOrchestration, call.OrchestratorName, call.Options,
                async number =>
                {
                    var child = Start(new TaskName(call.OrchestratorName), AttemptId(number), input, options?.Version, parent, parent.Token);
                    return await WaitForOutputAsync(child, parent.Token).ConfigureAwait(false);
                }, AttemptId).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _childExecutions.Remove(drained.Task);
            }

            drained.TrySetResult();
        }
    }

    private void RouteEvent(string instanceId, string eventName, object? payload)
    {
        var instance = Find(instanceId);
        lock (_gate)
        {
            if (instance.Status is not (OrchestrationRuntimeStatus.Pending or OrchestrationRuntimeStatus.Running))
            {
                return;
            }
        }

        var captured = CapturePayload(payload);
        lock (_gate)
        {
            if (instance.Status is OrchestrationRuntimeStatus.Pending or OrchestrationRuntimeStatus.Running)
            {
                instance.Context.RaiseEvent(eventName, captured);
            }
        }
    }

    private OrchestrationMetadata? ReadMetadata(string id, bool includePayloads)
    {
        WorkflowInstance instance;
        lock (_gate)
        {
            EnsureAvailable();
            if (!_instances.TryGetValue(id, out instance!))
            {
                return null;
            }
        }

        return BuildMetadata(instance, includePayloads);
    }

    private OrchestrationMetadata BuildMetadata(WorkflowInstance instance, bool includePayloads)
    {
        OrchestrationRuntimeStatus status;
        object? output;
        Exception? failure;
        lock (_gate)
        {
            status = instance.Status;
            output = instance.Output;
            failure = instance.Failure;
        }

        var builder = new TestOrchestrationMetadataBuilder(instance.Name.Name, instance.Context.InstanceId)
            .WithRuntimeStatus(status)
            .WithTimestamps(instance.CreatedAt, new DateTimeOffset(instance.Context.CurrentUtcDateTime))
            .WithDataConverter(instance.Context.PayloadConverter ?? JsonDataConverter.Default);
        if (includePayloads)
        {
            builder.WithInput(instance.Input).WithOutput(output).WithCustomStatus(instance.Context.MetadataCustomStatus);
            if (failure is not null && status == OrchestrationRuntimeStatus.Failed)
            {
                builder.WithFailure(failure);
            }
        }

        return builder.Build(includePayloads);
    }
}
