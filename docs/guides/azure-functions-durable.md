# Test Durable orchestrators in memory

`TestOrchestrationContext` supports a focused unit-test boundary for .NET isolated Durable
orchestrators. It supports typed input, instance IDs, a replay flag, manually advanced time,
buffered external events, and recorded activity and sub-orchestrator dispatch.
Remaining context members have lightweight defaults with fluent overrides for behavior needed by a test.

For complete scenarios, see the [orchestration recipes](azure-functions-durable-recipes.md): partial
batch failure, approval/timeouts, isolated child fan-out, and explicit paged continuations.

## Dispatch to a real activity

Provide an async handler that maps recorded calls to real activity instances or explicit test
results:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableFunctionTests.cs#docs-functions-durable-activity -->
```csharp
[Fact]
public async Task Orchestrator_dispatches_to_real_activity_and_records_call()
{
    var activity = new GreetingActivity("Hello");
    var context = new TestOrchestrationContext(async call =>
    {
        Assert.Equal(nameof(GreetingActivity), call.ActivityName);
        Assert.Equal(typeof(string), call.ResultType);
        return await activity.RunAsync(Assert.IsType<string>(call.Input));
    });

    var result = await GreetingOrchestrator.RunAsync(context);

    Assert.Equal("Hello, Ada!", result);
    var call = Assert.Single(context.ActivityCalls);
    Assert.Equal(nameof(GreetingActivity), call.ActivityName);
    Assert.Equal("Ada", call.Input);
    Assert.Null(call.Options);
}
```
<!-- end-snippet -->

`ActivityCalls` preserves scheduling order and exposes the task name, input, result type, and
options. The handler must return a value compatible with the generic result type; null for a
non-nullable value type and incompatible results fail with `InvalidOperationException`.
The parameterless constructor instead returns `default(TResult)` for activity and child calls,
including zero-initialized structs, without executing activity or child implementations.

## Route activities by name

`WithActivityHandler(activityName, handler)` registers a handler receiving the activity name and
original input. Names use case-sensitive ordinal matching. Named handlers take precedence over the
constructor callback or `WithActivityHandler(handler)` fallback, regardless of registration order.
Registering the same name again replaces its handler for future calls; in-flight calls retain the
handler selected when they were recorded.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableNamedActivityHandlerTests.cs#docs-functions-durable-named-activities -->
```csharp
[Fact]
public async Task Named_handlers_dispatch_matching_activities_and_preserve_fallback()
{
    var context = new TestOrchestrationContext()
        .WithActivityHandler(_ => Task.FromResult<object?>("fallback"))
        .WithActivityHandler("Greeting", (name, input) =>
            Task.FromResult<object?>($"{name}: Hello, {input}!"))
        .WithActivityHandler("Double", (_, input) =>
            Task.FromResult<object?>(Assert.IsType<int>(input) * 2));

    Assert.Equal("Greeting: Hello, Ada!", await context.CallActivityAsync<string>("Greeting", "Ada"));
    Assert.Equal(42, await context.CallActivityAsync<int>("Double", 21));
    Assert.Equal("fallback", await context.CallActivityAsync<string>("Other"));
    Assert.Equal(["Greeting", "Double", "Other"], context.ActivityCalls.Select(call => call.ActivityName));
}
```
<!-- end-snippet -->

Named calls are recorded in `ActivityCalls` with their input, options, and requested result type.
Result validation and exception propagation are the same as for the fallback handler.

## Configure input and identity

Use `WithInput(request)` before execution to supply `GetInput<T>()`, and `WithInstanceId("parent-42")`
to supply `InstanceId`. The default instance ID is `test-orchestration`. Input is retained by reference
without serialization: use an assignable type when reading it. Absent input returns `default(T)`;
an incompatible non-null value throws `InvalidOperationException`.

## Drive timers, events, and child orchestrators

This workflow reads input, waits for a simulated timer, receives approval, and calls a child:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableOrchestrationControlTests.cs#docs-functions-durable-workflow -->
```csharp
private static async Task<int> RunWorkflowAsync(TaskOrchestrationContext context, CancellationToken cancellationToken)
{
    var input = context.GetInput<WorkRequest>();
    await context.CreateTimer(TimeSpan.FromMinutes(5), cancellationToken);
    var approved = await context.WaitForExternalEvent<bool>("Approved", cancellationToken);
    return approved ? await context.CallSubOrchestratorAsync<int>("Child", input) : 0;
}

private sealed record WorkRequest(int Id);
```
<!-- end-snippet -->

The test controls time and delivery explicitly. Events raised before the wait are buffered:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableOrchestrationControlTests.cs#docs-functions-durable-controls -->
```csharp
[Fact]
public async Task Orchestrator_uses_input_time_events_and_child_dispatch()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var context = new TestOrchestrationContext(_ => Task.FromResult<object?>(null))
        .WithInput(new WorkRequest(42))
        .WithInstanceId("parent-42")
        .WithReplayState(false);
    context.SubOrchestratorHandler = call => Task.FromResult<object?>(Assert.IsType<WorkRequest>(call.Input).Id);

    var run = RunWorkflowAsync(context, cancellationToken);
    Assert.False(run.IsCompleted);
    context.RaiseEvent("approved", true);
    context.AdvanceTimeBy(TimeSpan.FromMinutes(5));

    Assert.Equal(42, await run.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
    Assert.Equal("Child", Assert.Single(context.SubOrchestrationCalls).OrchestratorName);
    Assert.Equal(DateTime.UnixEpoch.AddMinutes(5), context.CurrentUtcDateTime);
}
```
<!-- end-snippet -->

- `CurrentUtcDateTime` starts at `DateTime.UnixEpoch`. `AdvanceTimeTo(utcDateTime)` and
  `AdvanceTimeBy(duration)` move it forward and complete timers already scheduled on or before that
  time. Times must be UTC and cannot move backwards. Past or current deadlines complete immediately.
- Timer continuations run asynchronously. If a resumed workflow schedules another relative timer,
  that timer starts from the new current time; advance again after the next timer is scheduled.
- `RaiseEvent(name, payload)` completes the oldest matching wait or buffers the payload. Names are
  case-insensitive; each event completes one wait in FIFO order. Canceled waits do not consume future events.
- Timer and event waits propagate cancellation tokens. The inherited event timeout overload also uses
  simulated time and cancels the wait with `OperationCanceledException` when the timeout expires.
- `SendEvent(instanceId, name, payload)` records `SentEvents`. Self-directed events are delivered
  locally. For other instances, the test routes recorded events to the target context's `RaiseEvent`.
- `CallSubOrchestratorAsync<T>()` records `SubOrchestrationCalls` and invokes `SubOrchestratorHandler`.
  The default handler returns `default(T)`. A configured handler can return explicit results or create another context
  with its own input and instance ID and invoke the real child orchestrator. Options, including any
  child instance ID, are recorded without executing retry policies. Handler failures propagate unchanged.

Input, activity results, event payloads, and child results use direct assignment without a serialization
round trip. Incompatible results fail with `InvalidOperationException`; null results require a nullable type.
Recorded-call collections are live views; inspect or enumerate them after the relevant execution completes.

## Replay state and logging

`IsReplaying` defaults to `false`. `WithReplayState(true)` allows replay-aware branches to be tested;
it does not replay history, deduplicate calls, or replace handler execution with cached results.

`CreateReplaySafeLogger<T>()`, the category-name and type overloads, and `ReplaySafeLoggerFactory`
use no-op logging by default. To inspect logs, call `WithLoggerFactory(factory)` before creating
loggers. The factory remains caller-owned. Replay-safe loggers suppress output while `IsReplaying`
is true, including when the flag changes after logger creation.

## Default stubs and fluent overrides

Metadata and runtime-dependent members can be used without setting up a Durable runtime:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableFunctionTests.cs#docs-functions-durable-limitations -->
```csharp
[Fact]
public void Runtime_dependent_members_have_in_memory_defaults()
{
    var context = new TestOrchestrationContext();

    Assert.NotNull(context.Entities);
    Assert.Equal("TestOrchestrator", context.Name.Name);
    Assert.Null(context.Parent);
    Assert.Empty(context.Version);
    Assert.Empty(context.Properties);
    context.ContinueAsNew();
    Assert.Single(context.ContinueAsNewRequests);
    Assert.NotEqual(Guid.Empty, context.NewGuid());
}
```
<!-- end-snippet -->

Use `With...` methods to replace the defaults:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableOrchestrationStubTests.cs#docs-functions-durable-overrides -->
```csharp
[Fact]
public async Task Fluent_overrides_replace_in_memory_defaults()
{
    var expectedGuid = Guid.Parse("3c91271a-a927-4516-b225-41cce45963b5");
    var parent = new ParentOrchestrationInstance("Parent", "parent-42");
    var context = new TestOrchestrationContext()
        .WithName("ProcessOrder")
        .WithParent(parent)
        .WithVersion("2.0")
        .WithProperties(new Dictionary<string, object?> { ["tenant"] = "test" })
        .WithGuidGenerator(() => expectedGuid)
        .WithActivityHandler(_ => Task.FromResult<object?>(42))
        .WithSubOrchestratorHandler(_ => Task.FromResult<object?>("completed"))
        .WithCustomStatus("ready");

    Assert.Equal("ProcessOrder", context.Name.Name);
    Assert.Same(parent, context.Parent);
    Assert.Equal("2.0", context.Version);
    Assert.Equal("test", context.Properties["tenant"]);
    Assert.Equal(expectedGuid, context.NewGuid());
    Assert.Equal(42, await context.CallActivityAsync<int>("Count"));
    Assert.Equal("completed", await context.CallSubOrchestratorAsync<string>("Child"));
    Assert.Equal("ready", context.CustomStatus);
}
```
<!-- end-snippet -->

| Member | Default behavior | Override or assertions |
| --- | --- | --- |
| `Name` | `TestOrchestrator` | `WithName(taskName)` |
| `Parent` | `null` | `WithParent(parent)`, including null to clear |
| `Version` | Empty string | `WithVersion(version)` |
| `Properties` | Empty read-only dictionary | `WithProperties(dictionary)` shallow-copies configuration |
| `Entities` | Calls return `default(T)`, signals complete, lock scopes maintain local critical-section markers | `WithEntities(feature)` supplies custom entity behavior |
| `NewGuid()` | Repeatable per-instance sequence | `WithGuidGenerator(generator)` |
| `CompareVersionTo(version)` | Ordinal comparison of current and supplied version | `WithVersionComparer(comparer)` for numeric or application-specific ordering |
| `SetCustomStatus(status)` | Retains the latest value in `CustomStatus`; null clears it | `WithCustomStatus(status)`, `WithCustomStatusHandler(handler)` |
| `ContinueAsNew(...)` | Copies options into `ContinueAsNewRequests`, without restarting | `WithContinueAsNewHandler(handler)` |
| Activity dispatch | Typed defaults with the parameterless constructor | `WithActivityHandler(handler)` or the existing constructor |
| Child dispatch | Typed defaults | `WithSubOrchestratorHandler(handler)` or the existing property |

Configure metadata and handlers before execution. Status and continuation callbacks run after recording;
their exceptions propagate. Continuation input references are retained without cloning, while option
values are copied at invocation. A continuation request leaves current input, version, time, and event
queues unchanged, regardless of its event-preservation flag.

The default GUID sequence is repeatable for matching instance IDs and call order, but does not reproduce
the Durable runtime's GUID algorithm. Entity stubs do not execute entity logic, persist state, or acquire
distributed locks; overlapping local scopes only affect `InCriticalSection` until disposed.

## Runtime boundary

The test context does not emulate:

- Durable history or deterministic replay.
- Persistence, history-based result reuse, retry execution, or child-instance lifecycle management.
- Entity state, distributed locks, suspension, termination, purge, or instance management.
- The production runtime's GUID algorithm or actual continuation restarts.

Those behaviors require the real Durable runtime or a dedicated runtime-level harness. Do not use
this helper to claim replay safety or production scheduling correctness.

## Troubleshooting dispatch

- An unexpected activity should make the handler throw with the recorded activity name.
- A null result for a value type or a mismatched result type produces an explicit dispatch error.
- If default results are insufficient, replace activity or child handlers or provide a custom entity
  feature. Stubs preserve test execution without providing runtime behavior.

See [`DurableFunctionTests`](../../tests/TestFunctions.IntegrationTests/DurableFunctionTests.cs) for
handler validation, and
[`DurableOrchestrationControlTests`](../../tests/TestFunctions.IntegrationTests/DurableOrchestrationControlTests.cs)
for timing, cancellation, event buffering, child dispatch, and replay logging coverage.
See [`DurableOrchestrationStubTests`](../../tests/TestFunctions.IntegrationTests/DurableOrchestrationStubTests.cs)
for defaults, fluent overrides, entity scopes, and continuation/status recording.

See the [Azure Functions API reference](../api/packages/xbullet-easytesting-azurefunctions.md) for
`TestOrchestrationContext` and `RecordedActivityCall`.
