# Durable retry and failure-sequence tests

`TestDurableTaskSequence<TResult>` supplies explicit result/failure outcomes to test handlers.
`TestDurableWorkflow.WithRetries()` opts connected activities and child orchestrations into bounded
simulation of SDK `TaskOptions.Retry`. Default workflow behavior still performs one attempt and
records retry options without executing them. Standalone orchestration contexts retain their
existing handler behavior.

## Control retry timing

Configure `WithRetries` before scheduling, then pass an SDK `RetryPolicy` in the task options.
Policy delays use the calling context's manual UTC clock. Tests advance that clock explicitly;
retry execution does not introduce wall-clock sleeps or automatically advance time.

<!-- snippet: tests/TestFunctions.IntegrationTests/ConnectedDurableWorkflowRetryTests.cs#docs-functions-connected-retries -->
```csharp
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
```
<!-- end-snippet -->

The `retryScheduled` observer receives the failed attempt after `RetryAt` has been recorded and
before the next attempt. It runs outside workflow locks and can signal the test or advance the
calling context directly. The due time is fixed before invoking the observer, so advancing to it
there is safe even if the retry timer has not yet been created. Observer failures stop the logical
call; they do not invoke another task attempt.

## Supported SDK options

| Option | Simulation behavior |
| --- | --- |
| `MaxNumberOfAttempts` | Includes the first attempt; exhaustion rethrows the original last failure |
| `FirstRetryInterval` | Delay after the first failed attempt |
| `BackoffCoefficient` | Multiplies later delays exponentially |
| `MaxRetryInterval` | Caps each delay; infinite intervals remain bounded by the manual clock's range |
| `RetryTimeout` | Stops new attempts at or after the calling clock's deadline; does not interrupt an active task |
| `HandleFailure` | Receives SDK failure details and may accept or reject the retry |
| `TaskOptions.FromRetryHandler` | Receives SDK retry context and controls its own delay and decision |

`WithRetries(maximumAttempts: 100)` also enforces a finite per-call safety limit, including the
first attempt. The smaller policy or workflow limit wins. Custom handlers that always return true
are bounded by the workflow limit. A call without retry options still executes once.

Policy limits and deadlines take precedence over failure filters. A delay reaching the deadline
does not start another attempt. If a test advances beyond the deadline while a delay is pending,
the original failure is returned without invoking the task again. Timeout calculations use the
calling context's clock, including any manual time advanced during earlier attempts.

Custom retry handlers receive the calling `OrchestrationContext`, one-based `LastAttemptNumber`,
`LastFailure`, manual `TotalRetryTime`, and execution `CancellationToken`. They can await the
context's timers to implement delay. Returning true adds no further policy delay. Handler and
filter exceptions propagate. The SDK's obsolete `RetryPolicy.HandleAsync` is not implemented;
use `TaskOptions.FromRetryHandler` for asynchronous decisions.

Cancellation exceptions and `TestDurableTaskSequenceExhaustedException` are terminal. Cleanup
cancels pending retry timers and the token provided to custom handlers. User code that ignores
cancellation is subject to the workflow's existing bounded cleanup behavior.

## Inspect attempts and invocation scopes

`workflow.Attempts` returns immutable ordered snapshots. Every invoked activity or child has an
attempt record, even when retry simulation is disabled. Each record includes:

- Workflow-wide `Sequence` and logical `CallId`.
- Calling instance ID, task name, category, and one-based attempt number.
- Actual child instance ID, when applicable.
- Manual start/completion times, status, planned retry time, and original exception.

The originating context retains one `ActivityCalls` or `SubOrchestrationCalls` entry per logical
call. Retrying an activity creates a fresh host invocation and DI scope for each attempt. Retrying
a child creates a new context and scope. The first child uses its requested/generated ID; subsequent
attempts use `base-id:retry:2`, `base-id:retry:3`, and so on. These IDs are harness conventions,
and failed child instances remain inspectable until reset. Parent metadata and requested child
version are retained. Child clocks remain independent; advancing one does not advance its parent.

Input is captured once per logical call when [serialization is enabled](azure-functions-durable-workflows.md).
Each retry deserializes a fresh activity input, so mutations in a failed attempt do not alter later
inputs. Direct assignment mode retains shared references across attempts. Producer failures,
including output serialization failures, may be retried; a receiver's result-conversion failure
does not rerun a producer that already completed successfully. Missing registrations and input
serialization failures are submission failures rather than retryable task attempts.

Diagnostics include attempt order, IDs, statuses, timestamps, and planned retries. They exclude
inputs, outputs, and failure contents. The explicit `Attempts` API retains original exceptions
for assertions and may therefore expose application failure text.

## Configure and reset outcome sequences

`ThenThrow(exception)` and `ThenReturn(value)` append outcomes before consumption starts or after
reset. `NextAsync(token)` consumes one step and returns its original result reference or original
exception. Null results are allowed for suitable result types. Concurrent calls reserve distinct
steps in invocation order; completion order is not controlled. Sharing one sequence across logical
calls also shares that consumption order. Use separate sequences when calls need independent scripts.

Exhaustion fails explicitly and does not consume a step. Canceled submissions do not consume steps.
`ConsumedCount` tracks configured steps already reserved. Sequence diagnostics contain only counts.
Reset rewinds consumption and preserves all configured outcomes.

A workflow borrows sequences captured by delegates and does not reset them automatically. Await
successful `workflow.ResetAsync(token)` before resetting a sequence used by that workflow. In
scenario tests, register the workflow as the resource and rewind dependent sequences in the next
scenario's arrangement, after the initial workflow reset has drained prior work. Standalone
sequences implement `ITestScenarioResource` and can be registered directly for scenario resets.

Workflow reset clears attempts and generated call/attempt IDs while preserving registrations,
retry configuration, and the observer. Configuration freezes at the first successful schedule and
can be changed after reset succeeds. Failed cleanup retains attempt diagnostics. Cleanup tracks
logical activity and child calls, including retry handlers still running after a parent returns.

This simulation does not implement durable history, persistence, replay, backend delivery,
transactional rollback, or production child-retry identity. It does not retry roots, client
management calls, entity operations, or standalone context handlers automatically. Re-executed
application code keeps its side effects; use runtime integration tests for Durable guarantees.
