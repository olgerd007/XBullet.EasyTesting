# Durable orchestration recipes

These examples combine named activity handlers, real example workflow and activity objects, input,
status, manual time, external events, child contexts, and recorded continuation requests.
They run without starting the Functions host or connecting to external services.

The complete executable source is
[`DurableWorkflowExamples.cs`](../../tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs).
Each workflow, request, result, and activity below belongs to that test class. Snippets are synchronized
from the compiled source. Start with the [context guide](azure-functions-durable.md) for defaults and configuration.

Run these examples from the repository root:

```powershell
dotnet test `
  --project tests/TestFunctions.IntegrationTests/TestFunctions.IntegrationTests.csproj `
  --filter-class "*DurableWorkflowExamples"
```

The examples use these imports and a bounded wait for test completion:

```csharp
using System.Collections.Concurrent;
using Microsoft.DurableTask;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
```

`TestTimeout` is a wall-clock guard against a hanging test. Orchestration timers are advanced explicitly
with `AdvanceTimeBy`; the guard does not move simulated time.

| Recipe | Behavior demonstrated |
| --- | --- |
| [Partial batch failure](#continue-after-one-authorization-batch-fails) | Named dispatch, real activity invocation, failure handling, progress, and outbound event assertions |
| [Approval and timeout](#race-approval-against-a-manual-timer) | Approval, rejection, timeout, cancellation of losing waits, and late-event buffering |
| [Child fan-out](#fan-out-to-isolated-child-orchestrators) | Concurrent child dispatch, parent metadata, separate contexts, and combined results |
| [Paged continuation](#drive-paged-continuations-explicitly) | Recorded next input/version and a manually created second execution |

## Continue after one authorization batch fails

Register a strict fallback to expose unexpected activity names. One named handler returns the batches;
the other invokes an activity object for successful batches and injects a failure for batch 2.
The orchestrator continues with batch 3 and reports both successful and failed IDs.

The in-memory dispatcher propagates the exception supplied by the handler. This test injects
`TaskFailedException` explicitly to exercise the failure shape caught by the orchestrator; it does
not emulate runtime exception wrapping or retries. The audit event is recorded in `SentEvents`;
delivery to another instance remains under test control.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-batch-test -->
```csharp
[Fact]
public async Task Authorization_updates_continue_after_one_batch_fails()
{
    var runId = Guid.Parse("3c91271a-a927-4516-b225-41cce45963b5");
    var activity = new UpdateAuthorizationActivity();
    var batches = new[] { new AuthorizationBatch(1), new AuthorizationBatch(2), new AuthorizationBatch(3) };
    var progress = new List<object?>();
    var context = new TestOrchestrationContext()
        .WithActivityHandler(call => throw new InvalidOperationException($"Unexpected activity: {call.ActivityName}"))
        .WithInstanceId("authorization-42")
        .WithGuidGenerator(() => runId)
        .WithCustomStatusHandler(progress.Add)
        .WithActivityHandler("LoadBatches", (_, _) => Task.FromResult<object?>(batches))
        .WithActivityHandler("UpdateAuthorization", async (_, input) =>
        {
            var batch = Assert.IsType<AuthorizationBatch>(input);
            if (batch.Id == 2)
            {
                throw new TaskFailedException("UpdateAuthorization", 2, new InvalidOperationException("Batch rejected."));
            }

            await activity.RunAsync(batch);
            return null;
        });

    var result = await UpdateAuthorizationBatchesAsync(context);

    Assert.Equal(runId, result.RunId);
    Assert.Equal([1, 3], result.Succeeded);
    Assert.Equal([2], result.Failed);
    Assert.Equal([1, 3], activity.UpdatedBatchIds);
    Assert.Equal(["LoadBatches", "UpdateAuthorization", "UpdateAuthorization", "UpdateAuthorization"],
        context.ActivityCalls.Select(call => call.ActivityName));
    Assert.Equal(new BatchProgress(3, 3, 1), Assert.IsType<BatchProgress>(progress.Last()));
    var audit = Assert.Single(context.SentEvents);
    Assert.Equal("audit", audit.InstanceId);
    Assert.Equal("BatchFailed", audit.EventName);
    Assert.Equal(2, Assert.IsType<AuthorizationBatch>(audit.Payload).Id);
}
```
<!-- end-snippet -->

The workflow, activity, and models:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-batch-workflow -->
```csharp
private static async Task<BatchUpdateReport> UpdateAuthorizationBatchesAsync(TaskOrchestrationContext context)
{
    var runId = context.NewGuid();
    var batches = await context.CallActivityAsync<AuthorizationBatch[]>("LoadBatches")
        ?? throw new InvalidOperationException("Authorization batches are required.");
    var succeeded = new List<int>();
    var failed = new List<int>();
    foreach (var batch in batches)
    {
        try
        {
            await context.CallActivityAsync("UpdateAuthorization", batch);
            succeeded.Add(batch.Id);
        }
        catch (TaskFailedException)
        {
            failed.Add(batch.Id);
            context.SendEvent("audit", "BatchFailed", batch);
        }

        context.SetCustomStatus(new BatchProgress(succeeded.Count + failed.Count, batches.Length, failed.Count));
    }

    return new BatchUpdateReport(runId, succeeded.ToArray(), failed.ToArray());
}

private sealed class UpdateAuthorizationActivity
{
    public List<int> UpdatedBatchIds { get; } = [];

    public Task RunAsync(AuthorizationBatch batch)
    {
        UpdatedBatchIds.Add(batch.Id);
        return Task.CompletedTask;
    }
}

private sealed record AuthorizationBatch(int Id);
private sealed record BatchProgress(int Completed, int Total, int Failed);
private sealed record BatchUpdateReport(Guid RunId, int[] Succeeded, int[] Failed);
```
<!-- end-snippet -->

## Race approval against a manual timer

The same workflow completes an approved order, stops on rejection, or times out after ten simulated
minutes. Both waits use linked cancellation tokens; the `finally` block cancels the losing timer or
event wait on every exit path. Awaiting the winning timer preserves caller cancellation instead of
classifying it as a business timeout.

The approval and rejection cases finish without advancing time. The timeout case moves the clock in
two steps and proves that an event raised afterward is available to a new wait.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-approval-test -->
```csharp
[Theory]
[InlineData(true)]
[InlineData(false)]
public async Task Approval_or_rejection_cancels_the_losing_timer(bool approved)
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var context = new TestOrchestrationContext()
        .WithInput(new ApprovalRequest(42))
        .WithActivityHandler("CompleteOrder", (_, input) =>
        {
            Assert.Equal(42, Assert.IsType<ApprovalRequest>(input).OrderId);
            return Task.FromResult<object?>(null);
        });

    var run = AwaitOrderApprovalAsync(context, cancellationToken);
    Assert.False(run.IsCompleted);
    context.RaiseEvent("approval", approved);
    var result = await run.WaitAsync(TestTimeout, cancellationToken);

    Assert.Equal(approved ? ApprovalOutcome.Completed : ApprovalOutcome.Rejected, result);
    Assert.Equal(approved ? 1 : 0, context.ActivityCalls.Count);
    Assert.Equal(approved ? "completed" : "rejected", context.CustomStatus);
    context.AdvanceTimeBy(TimeSpan.FromMinutes(10));
    Assert.Equal(approved ? "completed" : "rejected", context.CustomStatus);
}

[Fact]
public async Task Missing_approval_times_out_without_consuming_a_later_event()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var context = new TestOrchestrationContext().WithInput(new ApprovalRequest(42));
    var run = AwaitOrderApprovalAsync(context, cancellationToken);
    context.AdvanceTimeBy(TimeSpan.FromMinutes(9));
    Assert.False(run.IsCompleted);
    context.AdvanceTimeBy(TimeSpan.FromMinutes(1));

    Assert.Equal(ApprovalOutcome.TimedOut, await run.WaitAsync(TestTimeout, cancellationToken));
    Assert.Equal("timed-out", context.CustomStatus);
    Assert.Empty(context.ActivityCalls);
    context.RaiseEvent("Approval", true);
    Assert.True(await context.WaitForExternalEvent<bool>("Approval", cancellationToken)
        .WaitAsync(TestTimeout, cancellationToken));
}
```
<!-- end-snippet -->

The workflow and models:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-approval-workflow -->
```csharp
private static async Task<ApprovalOutcome> AwaitOrderApprovalAsync(
    TaskOrchestrationContext context,
    CancellationToken cancellationToken)
{
    var request = context.GetInput<ApprovalRequest>()
        ?? throw new InvalidOperationException("Approval input is required.");
    using var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    using var eventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var approval = context.WaitForExternalEvent<bool>("Approval", eventCancellation.Token);
    var timer = context.CreateTimer(TimeSpan.FromMinutes(10), timerCancellation.Token);
    try
    {
        context.SetCustomStatus("awaiting-approval");
        var winner = await Task.WhenAny(approval, timer);
        if (winner == timer)
        {
            await timer;
            context.SetCustomStatus("timed-out");
            return ApprovalOutcome.TimedOut;
        }

        if (!await approval)
        {
            context.SetCustomStatus("rejected");
            return ApprovalOutcome.Rejected;
        }

        await context.CallActivityAsync("CompleteOrder", request);
        context.SetCustomStatus("completed");
        return ApprovalOutcome.Completed;
    }
    finally
    {
        timerCancellation.Cancel();
        eventCancellation.Cancel();
    }
}

private sealed record ApprovalRequest(int OrderId);
private enum ApprovalOutcome { Completed, Rejected, TimedOut }
```
<!-- end-snippet -->

## Fan out to isolated child orchestrators

The parent creates one child call per order and awaits all receipts. Its handler creates a separate
context for each child, forwarding input, the requested child instance ID, and parent metadata.
Each child routes calculation and receipt persistence through its own named handlers.

A completion source holds calculation until all three children have started. This demonstrates
overlapping execution without sleeps. Concurrent collections capture results safely; assertions
inspect each child's calls and status after the parent completes.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-children-test -->
```csharp
[Fact]
public async Task Parent_fans_out_to_isolated_child_contexts_and_collects_receipts()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var pricingReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var children = new ConcurrentDictionary<string, TestOrchestrationContext>();
    var savedReceipts = new ConcurrentBag<OrderReceipt>();
    var pricing = new CalculateOrderTotalActivity();
    var orders = new[] { new OrderLine(1, 2, 10m), new OrderLine(2, 3, 30m), new OrderLine(3, 1, 12.50m) };
    var parent = new TestOrchestrationContext().WithName("PriceOrders").WithInstanceId("pricing-42").WithInput(orders);
    parent.WithSubOrchestratorHandler(async call =>
    {
        Assert.Equal("PriceOrder", call.OrchestratorName);
        var options = Assert.IsType<SubOrchestrationOptions>(call.Options);
        var child = new TestOrchestrationContext()
            .WithName("PriceOrder")
            .WithInstanceId(options.InstanceId!)
            .WithParent(new ParentOrchestrationInstance(parent.Name, parent.InstanceId))
            .WithInput(call.Input)
            .WithActivityHandler("CalculateTotal", async (_, input) =>
            {
                await pricingReady.Task.WaitAsync(cancellationToken);
                return pricing.Run(Assert.IsType<OrderLine>(input));
            })
            .WithActivityHandler("SaveReceipt", (_, input) =>
            {
                savedReceipts.Add(Assert.IsType<OrderReceipt>(input));
                return Task.FromResult<object?>(null);
            });
        Assert.True(children.TryAdd(child.InstanceId, child));
        return await PriceOrderAsync(child);
    });

    var run = PriceOrdersAsync(parent);
    Assert.Equal(3, children.Count);
    Assert.False(run.IsCompleted);
    pricingReady.SetResult();
    var receipts = await run.WaitAsync(TestTimeout, cancellationToken);

    Assert.Equal([20m, 90m, 12.50m], receipts.Select(receipt => receipt.Total));
    Assert.Equal(3, savedReceipts.Count);
    Assert.Equal(3, parent.SubOrchestrationCalls.Count);
    Assert.Empty(parent.ActivityCalls);
    Assert.All(children.Values, child =>
    {
        Assert.Equal(parent.InstanceId, child.Parent!.InstanceId);
        Assert.Equal("saved", child.CustomStatus);
        Assert.Equal(["CalculateTotal", "SaveReceipt"], child.ActivityCalls.Select(call => call.ActivityName));
        Assert.Equal(child.InstanceId, Assert.IsType<OrderReceipt>(child.ActivityCalls[1].Input).InstanceId);
    });
}
```
<!-- end-snippet -->

The parent, child, activity, and models:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-children-workflow -->
```csharp
private static async Task<OrderReceipt[]> PriceOrdersAsync(TaskOrchestrationContext context)
{
    var orders = context.GetInput<OrderLine[]>()
        ?? throw new InvalidOperationException("Order input is required.");
    var calls = orders.Select(order => context.CallSubOrchestratorAsync<OrderReceipt>(
        "PriceOrder", order, new TaskOptions().WithInstanceId($"{context.InstanceId}-{order.Id}")));
    var receipts = await Task.WhenAll(calls);
    context.SetCustomStatus("all-orders-priced");
    return receipts;
}

private static async Task<OrderReceipt> PriceOrderAsync(TaskOrchestrationContext context)
{
    var order = context.GetInput<OrderLine>()
        ?? throw new InvalidOperationException("Order input is required.");
    var total = await context.CallActivityAsync<decimal>("CalculateTotal", order);
    var receipt = new OrderReceipt(order.Id, total, context.InstanceId);
    await context.CallActivityAsync("SaveReceipt", receipt);
    context.SetCustomStatus("saved");
    return receipt;
}

private sealed class CalculateOrderTotalActivity
{
    public decimal Run(OrderLine order) => order.Quantity * order.UnitPrice;
}

private sealed record OrderLine(int Id, int Quantity, decimal UnitPrice);
private sealed record OrderReceipt(int OrderId, decimal Total, string InstanceId);
```
<!-- end-snippet -->

## Drive paged continuations explicitly

The first execution imports two items and requests a continuation with a new cursor and version.
The test inspects the recorded options, verifies that the current input and version are unchanged,
and creates a second context from those options. The second execution imports the last item and
completes without requesting another restart.

`ContinueAsNew` records intent in this test context. The test driver creates the next execution;
it does not assume that history, events, or runtime state are automatically reset.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-pagination-test -->
```csharp
[Fact]
public async Task Paged_import_drives_recorded_continuations_as_explicit_new_executions()
{
    var stored = new List<int>();
    var continuations = new List<ContinueAsNewOptions>();
    var pages = new Dictionary<int, ImportPage>
    {
        [0] = new([10, 20], NextCursor: 1),
        [1] = new([30], NextCursor: null)
    };
    TestOrchestrationContext CreateExecution(ImportRequest input, string version) => new TestOrchestrationContext()
        .WithName("ImportPages")
        .WithInstanceId("import-42")
        .WithInput(input)
        .WithVersion(version)
        .WithContinueAsNewHandler(continuations.Add)
        .WithActivityHandler("LoadPage", (_, value) =>
            Task.FromResult<object?>(pages[Assert.IsType<ImportRequest>(value).Cursor]))
        .WithActivityHandler("StoreItem", (_, value) =>
        {
            stored.Add(Assert.IsType<int>(value));
            return Task.FromResult<object?>(null);
        });

    var first = CreateExecution(new ImportRequest(0), "v1");
    await ImportPageAsync(first);
    var restart = Assert.Single(first.ContinueAsNewRequests);
    Assert.Single(continuations);
    Assert.False(restart.PreserveUnprocessedEvents);
    Assert.Equal("v2", restart.NewVersion);
    Assert.Equal(0, first.GetInput<ImportRequest>().Cursor);
    Assert.Equal("v1", first.Version);
    Assert.Equal("continuation-requested", first.CustomStatus);

    var second = CreateExecution(Assert.IsType<ImportRequest>(restart.NewInput), restart.NewVersion!);
    await ImportPageAsync(second);
    Assert.Equal([10, 20, 30], stored);
    Assert.Empty(second.ContinueAsNewRequests);
    Assert.Single(continuations);
    Assert.Equal("v2", second.Version);
    Assert.Equal("completed", second.CustomStatus);
}
```
<!-- end-snippet -->

The workflow and models:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableWorkflowExamples.cs#docs-durable-pagination-workflow -->
```csharp
private static async Task ImportPageAsync(TaskOrchestrationContext context)
{
    var request = context.GetInput<ImportRequest>()
        ?? throw new InvalidOperationException("Import input is required.");
    var page = await context.CallActivityAsync<ImportPage>("LoadPage", request);
    foreach (var item in page.Items)
    {
        await context.CallActivityAsync("StoreItem", item);
    }

    if (page.NextCursor is int nextCursor)
    {
        context.SetCustomStatus("continuation-requested");
        context.ContinueAsNew(new ContinueAsNewOptions
        {
            NewInput = new ImportRequest(nextCursor),
            NewVersion = "v2",
            PreserveUnprocessedEvents = false
        });
        return;
    }

    context.SetCustomStatus("completed");
}

private sealed record ImportRequest(int Cursor);
private sealed record ImportPage(int[] Items, int? NextCursor);
```
<!-- end-snippet -->

See the [runtime boundary](azure-functions-durable.md#runtime-boundary) for behaviors that require a
Durable runtime suite, including history replay, durable persistence, retries, and lifecycle management.
