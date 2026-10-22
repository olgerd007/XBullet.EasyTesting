using System.Text.Json;
using System.Threading.Channels;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class ConnectedDurableWorkflowRetryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    #region docs-functions-connected-retries

    [Fact]
    public async Task Policy_retries_use_manual_time_and_record_each_attempt()
    {
        var sequence = new TestDurableTaskSequence<int>()
            .ThenThrow(new InvalidOperationException("temporary"))
            .ThenReturn(42);
        var retries = Channel.CreateUnbounded<RecordedDurableTaskAttempt>();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .WithRetries(retryScheduled: attempt => retries.Writer.TryWrite(attempt))
            .AddActivity<WorkflowFunction, object?, int>("Price", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Price",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(3, TimeSpan.FromSeconds(2)))));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        var result = workflow.GetResultAsync<int>(id, token);
        var retry = await retries.Reader.ReadAsync(token).AsTask().WaitAsync(Timeout, token);
        Assert.False(result.IsCompleted);

        workflow.GetContext(id).AdvanceTimeTo(retry.RetryAt!.Value);

        Assert.Equal(42, await result.WaitAsync(Timeout, token));
        Assert.Equal(new[] { DurableTaskAttemptStatus.Failed, DurableTaskAttemptStatus.Succeeded },
            workflow.Attempts.Select(attempt => attempt.Status));
        Assert.Equal(new[] { 1, 2 }, workflow.Attempts.Select(attempt => attempt.AttemptNumber));
        Assert.Single(workflow.GetContext(id).ActivityCalls);
    }

    public sealed class WorkflowFunction;

    #endregion

    [Fact]
    public async Task Retry_options_are_recorded_without_execution_until_enabled()
    {
        var failure = new InvalidOperationException("first failure");
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(failure).ThenReturn(42);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(4, TimeSpan.FromSeconds(1)))));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token)));
        Assert.Equal(1, sequence.ConsumedCount);
        Assert.Null(Assert.Single(workflow.Attempts).RetryAt);
    }

    [Fact]
    public async Task Exponential_backoff_is_capped_and_logical_calls_remain_single_recordings()
    {
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(new Exception()).ThenThrow(new Exception())
            .ThenThrow(new Exception()).ThenReturn(9);
        var retries = Channel.CreateUnbounded<RecordedDurableTaskAttempt>();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(retryScheduled: retry => retries.Writer.TryWrite(retry)).UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(4, TimeSpan.FromSeconds(2), 2, TimeSpan.FromSeconds(3)))));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        foreach (var seconds in new[] { 2, 5, 8 })
        {
            var retry = await retries.Reader.ReadAsync(token).AsTask().WaitAsync(Timeout, token);
            Assert.Equal(DateTime.UnixEpoch.AddSeconds(seconds), retry.RetryAt);
            workflow.GetContext(id).AdvanceTimeTo(retry.RetryAt!.Value);
        }

        Assert.Equal(9, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, workflow.Attempts.Select(attempt => attempt.Sequence));
        Assert.Single(workflow.Attempts.Select(attempt => attempt.CallId).Distinct());
        Assert.Single(workflow.GetContext(id).ActivityCalls);
        Assert.All(workflow.Attempts, attempt => Assert.NotNull(attempt.CompletedAt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Policy_limit_or_workflow_safety_limit_rethrows_last_failure(bool safetyLimit)
    {
        var first = new InvalidOperationException("first");
        var last = new InvalidOperationException("last");
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(first).ThenThrow(last).ThenReturn(99);
        TestDurableWorkflow? connected = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(safetyLimit ? 2 : 100,
                attempt => connected!.GetContext(attempt.InstanceId).AdvanceTimeTo(attempt.RetryAt!.Value)).UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(safetyLimit ? 5 : 2, TimeSpan.FromSeconds(1)))));
        connected = workflow;
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Same(last, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token)));
        Assert.Equal(2, sequence.ConsumedCount);
        Assert.Null(workflow.Attempts[1].RetryAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_filter_receives_original_failure_details(bool sdkFailure)
    {
        var failure = sdkFailure ? new TaskFailedException("Work", 7, new ArgumentException("private-failure"))
            : (Exception)new ArgumentException("private-failure");
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(failure).ThenReturn(42);
        TaskFailureDetails? observed = null;
        var policy = new RetryPolicy(3, TimeSpan.FromSeconds(1))
        {
            HandleFailure = details =>
            {
                observed = details;
                return false;
            }
        };
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries().UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work", options: TaskOptions.FromRetryPolicy(policy)));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        var actual = await Assert.ThrowsAnyAsync<Exception>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Same(failure, actual);
        Assert.NotNull(observed);
        Assert.True(observed.IsCausedBy<ArgumentException>());
        Assert.Contains("private-failure", observed.ErrorMessage);
        Assert.Single(workflow.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_timeout_prevents_new_attempts_at_or_after_deadline(bool leapBeyondDeadline)
    {
        var failure = new InvalidOperationException("timeout failure");
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(failure).ThenThrow(failure).ThenReturn(9);
        var retries = Channel.CreateUnbounded<RecordedDurableTaskAttempt>();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(retryScheduled: attempt => retries.Writer.TryWrite(attempt)).UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(10, TimeSpan.FromSeconds(1), retryTimeout: TimeSpan.FromSeconds(2)))));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        var retry = await retries.Reader.ReadAsync(token).AsTask().WaitAsync(Timeout, token);
        workflow.GetContext(id).AdvanceTimeTo(leapBeyondDeadline ? DateTime.UnixEpoch.AddSeconds(3) : retry.RetryAt!.Value);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token)));
        Assert.Equal(leapBeyondDeadline ? 1 : 2, sequence.ConsumedCount);
    }

    [Fact]
    public async Task Custom_retry_handler_receives_context_and_controls_manual_delays()
    {
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(new InvalidOperationException())
            .ThenThrow(new InvalidOperationException()).ThenReturn(8);
        var decisions = Channel.CreateUnbounded<Microsoft.DurableTask.RetryContext>();
        var options = TaskOptions.FromRetryHandler(async retry =>
        {
            var timer = retry.OrchestrationContext.CreateTimer(retry.OrchestrationContext.CurrentUtcDateTime.AddSeconds(1), retry.CancellationToken);
            decisions.Writer.TryWrite(retry);
            await timer;
            return true;
        });
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries().UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work", options: options));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        foreach (var number in new[] { 1, 2 })
        {
            var retry = await decisions.Reader.ReadAsync(token).AsTask().WaitAsync(Timeout, token);
            Assert.Equal(number, retry.LastAttemptNumber);
            Assert.Equal(TimeSpan.FromSeconds(number - 1), retry.TotalRetryTime);
            Assert.Same(workflow.GetContext(id), retry.OrchestrationContext);
            Assert.True(retry.CancellationToken.CanBeCanceled);
            Assert.True(retry.LastFailure.IsCausedBy<InvalidOperationException>());
            workflow.GetContext(id).AdvanceTimeBy(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(8, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(3, workflow.Attempts.Count);
    }

    [Fact]
    public async Task Children_retry_with_distinct_contexts_and_inspectable_attempt_ids()
    {
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(new InvalidOperationException()).ThenReturn(42);
        TestDurableWorkflow? connected = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(retryScheduled: attempt =>
            connected!.GetContext(attempt.InstanceId).AdvanceTimeTo(attempt.RetryAt!.Value)).UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Child", (_, context, worker) =>
            {
                Assert.Equal("parent", context.Parent!.InstanceId);
                Assert.Equal("v2", context.Version);
                return sequence.NextAsync(worker.CancellationToken);
            })
            .AddOrchestrator<WorkflowFunction, int>("Parent", (_, context, _) => context.CallSubOrchestratorAsync<int>("Child",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(3, TimeSpan.FromSeconds(1))).WithInstanceId("chosen") with { Version = "v2" }));
        connected = workflow;
        var token = TestContext.Current.CancellationToken;
        await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Parent", null, new() { InstanceId = "parent" }, token);
        Assert.Equal(42, await workflow.GetResultAsync<int>("parent", token).WaitAsync(Timeout, token));
        Assert.Equal(new[] { "chosen", "chosen:retry:2" }, workflow.Attempts.Select(attempt => attempt.ChildInstanceId));
        Assert.NotSame(workflow.GetContext("chosen"), workflow.GetContext("chosen:retry:2"));
        Assert.Equal(OrchestrationRuntimeStatus.Failed, (await workflow.Client.GetInstanceAsync("chosen", token))!.RuntimeStatus);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, (await workflow.Client.GetInstanceAsync("chosen:retry:2", token))!.RuntimeStatus);
        Assert.Single(workflow.GetContext("parent").SubOrchestrationCalls);
    }

    [Fact]
    public async Task Serialization_preserves_original_activity_input_across_fresh_scopes_and_retries()
    {
        var scopes = new List<ConnectedDurableWorkflowTests.ScopeMarker>();
        TestDurableWorkflow? connected = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().AddFunction<ScopedActivity>()
            .ConfigureServices(services => services.AddScoped<ConnectedDurableWorkflowTests.ScopeMarker>()).Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().WithRetries(retryScheduled: attempt =>
            connected!.GetContext(attempt.InstanceId).AdvanceTimeTo(attempt.RetryAt!.Value)).UseHost(host)
            .AddActivity<ScopedActivity, Payload, int>("Work", (function, input, _) =>
            {
                scopes.Add(function.Scope);
                Assert.Equal(2, input.Value);
                input.Value = 99;
                return scopes.Count == 1 ? Task.FromException<int>(new InvalidOperationException()) : Task.FromResult(7);
            })
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work", new { Value = 2 },
                TaskOptions.FromRetryPolicy(new RetryPolicy(2, TimeSpan.FromSeconds(1)))));
        connected = workflow;
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Equal(7, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(2, scopes.Distinct().Count());
        Assert.All(scopes, scope => Assert.True(scope.Disposed));
    }

    [Fact]
    public async Task Reset_cancels_retry_delay_without_consuming_next_outcome_and_clears_attempts()
    {
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(new InvalidOperationException("private-failure")).ThenReturn(4);
        var retries = Channel.CreateUnbounded<RecordedDurableTaskAttempt>();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(retryScheduled: attempt => retries.Writer.TryWrite(attempt)).UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(3, TimeSpan.FromHours(1)))));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await retries.Reader.ReadAsync(token).AsTask().WaitAsync(Timeout, token);
        var snapshot = await workflow.CaptureDiagnosticsAsync(token);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.Contains("RetryAt", json);
        Assert.DoesNotContain("private-failure", json);
        var completed = workflow.Client.WaitForInstanceCompletionAsync(id, cancellation: token);
        await workflow.ResetAsync(token);
        Assert.Equal(OrchestrationRuntimeStatus.Terminated, (await completed).RuntimeStatus);
        Assert.Equal(1, sequence.ConsumedCount);
        Assert.Empty(workflow.Attempts);
        Assert.Equal(json, JsonSerializer.Serialize(snapshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => workflow.WithRetries(0));
        workflow.WithRetries();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_and_sequence_exhaustion_are_terminal(bool exhausted)
    {
        var sequence = new TestDurableTaskSequence<int>();
        if (!exhausted)
        {
            sequence.ThenThrow(new OperationCanceledException()).ThenReturn(7);
        }

        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries().UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryHandler((Microsoft.DurableTask.RetryContext _) => true)));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(exhausted ? typeof(TestDurableTaskSequenceExhaustedException) : typeof(OperationCanceledException), error.GetType());
        Assert.Single(workflow.Attempts);
        Assert.Null(workflow.Attempts[0].RetryAt);
    }

    [Fact]
    public async Task Observer_failures_stop_retry_without_leaving_pending_timer()
    {
        var observerFailure = new InvalidOperationException("observer failed");
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(new Exception()).ThenReturn(4);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(retryScheduled: _ => throw observerFailure).UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryPolicy(new RetryPolicy(2, TimeSpan.FromSeconds(1)))));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Same(observerFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token)));
        Assert.Equal(1, sequence.ConsumedCount);
        await workflow.ResetAsync(token);
    }

    [Fact]
    public async Task Always_retrying_custom_handlers_are_bounded_and_configuration_survives_reset()
    {
        var failure = new InvalidOperationException("repeated");
        var sequence = new TestDurableTaskSequence<int>().ThenThrow(failure).ThenThrow(failure).ThenReturn(5);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(2).UseHost(host)
            .AddActivity<WorkflowFunction, object?, int>("Work", (_, _, worker) => sequence.NextAsync(worker.CancellationToken))
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, context, _) => context.CallActivityAsync<int>("Work",
                options: TaskOptions.FromRetryHandler((Microsoft.DurableTask.RetryContext _) => true)));
        var token = TestContext.Current.CancellationToken;
        for (var scenario = 0; scenario < 2; scenario++)
        {
            var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
            Assert.Throws<InvalidOperationException>(() => workflow.WithRetries());
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token)));
            Assert.Equal(2, sequence.ConsumedCount);
            Assert.Equal(2, workflow.Attempts.Count);
            await workflow.ResetAsync(token);
            await sequence.ResetAsync(token);
        }
    }

    [Fact]
    public async Task Cleanup_drains_unawaited_child_retry_handlers_and_preserves_attempts_on_timeout()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? background = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow(shutdownTimeout: TimeSpan.FromMilliseconds(50)).WithRetries().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Child", (_, _, _) => Task.FromException<int>(new InvalidOperationException("child failure")))
            .AddOrchestrator<WorkflowFunction, int>("Parent", (_, context, _) =>
            {
                background = context.CallSubOrchestratorAsync<int>("Child", options: TaskOptions.FromRetryHandler(async _ =>
                {
                    entered.TrySetResult();
                    await release.Task;
                    return true;
                }));
                return Task.FromResult(42);
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Parent", token);
        await entered.Task.WaitAsync(Timeout, token);
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => workflow.ResetAsync(token).AsTask());
            Assert.Single(workflow.Attempts);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => background!.WaitAsync(Timeout, token));
        await workflow.ResetAsync(token);
        Assert.Empty(workflow.Attempts);
    }

    [Fact]
    public async Task Concurrent_logical_calls_have_independent_attempt_numbers_and_snapshots()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestDurableWorkflow? connected = null;
        var counts = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithRetries(retryScheduled: attempt =>
            connected!.GetContext(attempt.InstanceId).AdvanceTimeTo(attempt.RetryAt!.Value)).UseHost(host)
            .AddActivity<WorkflowFunction, int, int>("Work", async (_, input, worker) =>
            {
                if (input == 1 && counts.IsEmpty)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(worker.CancellationToken);
                }

                if (counts.AddOrUpdate(input, 1, (_, count) => count + 1) == 1)
                {
                    throw new InvalidOperationException();
                }

                return input;
            })
            .AddOrchestrator<WorkflowFunction, int>("Order", async (_, context, _) =>
            {
                var options = TaskOptions.FromRetryPolicy(new RetryPolicy(2, TimeSpan.FromSeconds(1)));
                var first = context.CallActivityAsync<int>("Work", 1, options);
                var second = context.CallActivityAsync<int>("Work", 2, options);
                return (await first) + (await second);
            });
        connected = workflow;
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await entered.Task.WaitAsync(Timeout, token);
        var snapshot = workflow.Attempts.Single(attempt => attempt.CallId == 1);
        Assert.Equal(DurableTaskAttemptStatus.Running, snapshot.Status);
        release.SetResult();
        Assert.Equal(3, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(DurableTaskAttemptStatus.Running, snapshot.Status);
        Assert.Null(snapshot.CompletedAt);
        Assert.Equal(2, workflow.Attempts.GroupBy(attempt => attempt.CallId).Count());
        Assert.All(workflow.Attempts.GroupBy(attempt => attempt.CallId), attempts =>
            Assert.Equal(new[] { 1, 2 }, attempts.Select(attempt => attempt.AttemptNumber)));
    }

    public sealed class Payload
    {
        public int Value { get; set; }
    }

    public sealed class ScopedActivity(ConnectedDurableWorkflowTests.ScopeMarker scope)
    {
        public ConnectedDurableWorkflowTests.ScopeMarker Scope { get; } = scope;
    }
}
