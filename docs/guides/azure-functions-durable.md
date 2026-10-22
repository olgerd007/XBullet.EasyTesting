# Test Durable orchestrators in memory

`TestOrchestrationContext` supports a focused unit-test boundary for .NET isolated Durable
orchestrators. It supports typed input, instance IDs, a replay flag, manually advanced time,
buffered external events, and recorded activity and sub-orchestrator dispatch.
Remaining context members have lightweight defaults with fluent overrides for behavior needed by a test.

For complete scenarios, see the [orchestration recipes](azure-functions-durable-recipes.md): partial
batch failure, approval/timeouts, isolated child fan-out, and explicit paged continuations.

## Test HTTP starters

Use `TestDurableTaskClient` wherever a starter accepts `[DurableClient] DurableTaskClient`.
Pass it explicitly through the host's invocation delegate, or register it as `DurableTaskClient`
when the application uses constructor injection. The host does not resolve binding parameters
automatically. The worker Durable extension remains an application/test dependency for attributes
and HTTP helpers; the test package adds only `Microsoft.DurableTask.Client` for the client abstraction.

This test invokes an HTTP starter through the middleware pipeline and uses the SDK's real
`CreateCheckStatusResponseAsync` implementation:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableClientTests.cs#docs-functions-durable-client -->
```csharp
[Fact]
public async Task Http_starter_records_scheduling_and_returns_sdk_management_response()
{
    var builder = AzureFunctionTestHost.CreateBuilder();
    builder.AddFunction<StartOrderOrchestrationFunction>();
    await using var host = builder.Build();
    await using var client = new TestDurableTaskClient();
    var cancellationToken = TestContext.Current.CancellationToken;
    var request = host.HttpRequest(nameof(StartOrderOrchestrationFunction), cancellationToken)
        .WithMethod(HttpMethod.Post)
        .WithUrl("https://example.test/api/orders")
        .WithTextBody("order input")
        .Build();

    var invocation = await host.InvokeAsync<StartOrderOrchestrationFunction, HttpResponseData>(
        (TestFunctionContext)request.FunctionContext,
        (function, context) => function.RunAsync(request, client, context.CancellationToken));
    var response = Assert.IsAssignableFrom<HttpResponseData>(invocation.Result);
    var payload = await response.ReadBodyAsJsonAsync<JsonElement>(cancellationToken: cancellationToken);

    Assert.True(invocation.FunctionExecuted);
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.Equal("order-42", payload.GetProperty("id").GetString());
    Assert.Equal(
        payload.GetProperty("statusQueryGetUri").GetString(),
        Assert.Single(response.Headers.GetValues("Location")));
    Assert.StartsWith("https://example.test/runtime/webhooks/durabletask/instances/order-42",
        payload.GetProperty("statusQueryGetUri").GetString());
    var call = Assert.Single(client.ScheduledOrchestrations);
    Assert.Equal("ProcessOrder", call.OrchestratorName.Name);
    Assert.Equal("order input", call.Input);
    Assert.Equal("order-42", call.InstanceId);
}
```
<!-- end-snippet -->

The starter calls `ScheduleNewOrchestrationInstanceAsync` and returns the SDK response. Both
`CreateCheckStatusResponse` and `CreateCheckStatusResponseAsync` work with the test host's HTTP
request and worker serializer. Management links use the request origin; these links are not
served endpoints and do not include production binding credentials or task-hub query parameters.

- `ScheduledOrchestrations` returns a snapshot in scheduling order, including failed handler
  attempts. Each record contains the `TaskName`, input, options, and selected instance ID.
- Explicit `StartOrchestrationOptions.InstanceId` values are honored. Otherwise IDs are generated
  as `test-instance-1`, `test-instance-2`, and so on per client. Scheduling does not enforce
  deduplication or start-time policies. Input and options are retained by reference without cloning
  or serialization; the recorded instance ID is fixed when the call begins.
- `ScheduleHandler` receives the recorded attempt and caller token and can return a custom ID,
  throw a failure, or explicitly dispatch to application code. Its returned ID must be non-empty;
  it does not replace the originally recorded ID. Pre-canceled calls are neither recorded nor
  dispatched. Configure handlers before concurrent use.
- `GetInstanceHandler` receives the instance ID, payload inclusion flag, and caller token.
  Without a handler, lookups read explicitly seeded metadata and return null when none matches.
  Scheduling does not seed metadata automatically.
- `RaiseEventHandler`, `WaitForStartHandler`, `WaitForCompletionHandler`, `SuspendHandler`,
  `ResumeHandler`, and `QueryHandler` provide explicit test behavior for the other abstract client
  operations. Calling these without configuration throws `InvalidOperationException` identifying
  the missing handler. Handlers receive original arguments and own any simulated state or waits.
  Pageable query cancellation belongs to the returned `AsyncPageable`.
- Termination, purge, history, rewind, and restart have explicit handlers described below.
  `Entities` exposes a recording entity client; see [Durable Entity tests](azure-functions-durable-entities.md).
  Completion-time ID listing retains the SDK's unsupported behavior. Disposal owns no runtime
  resources and leaves recorded calls available for assertions.

Scheduling records intent without running an orchestrator. To exercise orchestration logic next,
create a separate `TestOrchestrationContext` using the recorded input and instance ID and invoke the
real orchestrator explicitly. Persistence, replay, retries, lifecycle behavior, and the management
HTTP endpoints still require runtime-level testing.

For automatic in-memory dispatch through the host's DI scopes and middleware, use the optional
[connected workflow harness](azure-functions-durable-workflows.md). It connects starters, named
orchestrators, activities, children, and external events without changing these standalone defaults.

## Record client management calls

`Operations` is a snapshot of every validated client call attempt, recorded before dispatch.
`Sequence` starts at one and represents invocation order, including failed and missing handlers;
it does not indicate completion or success. Pre-canceled and invalid calls are excluded.
`ScheduledOrchestrations` remains available for scheduling-specific assertions.

Each `RecordedDurableClientOperation` exposes an operation kind, target `InstanceId`, and read-only
`Arguments` keyed by the SDK parameter names below. Values retain original references, including
mutable inputs and options. `GetArgument<T>(name)` reads without conversion; missing arguments
throw `KeyNotFoundException`, mismatched non-null types throw `InvalidOperationException`, and
null arguments return `default(T)`. Cancellation tokens are propagated to handlers but not stored.

| Operation | Handler | Recorded arguments |
| --- | --- | --- |
| `Schedule` | `ScheduleHandler` | `orchestratorName`, `input`, `options` |
| `GetInstance` | `GetInstanceHandler` or seeded metadata | `getInputsAndOutputs` |
| `RaiseEvent` | `RaiseEventHandler` | `eventName`, `eventPayload` |
| `WaitForStart` | `WaitForStartHandler` | `getInputsAndOutputs` |
| `WaitForCompletion` | `WaitForCompletionHandler` | `getInputsAndOutputs` |
| `Suspend` | `SuspendHandler` | `reason` |
| `Resume` | `ResumeHandler` | `reason` |
| `Query` | `QueryHandler` | `filter` |
| `Terminate` | `TerminateHandler` | `options` |
| `PurgeInstance` | `PurgeHandler` | `options` |
| `PurgeAll` | `PurgeAllHandler` | `filter`, `options` |
| `Restart` | `RestartHandler` | `restartWithNewInstanceId` |
| `Rewind` | `RewindHandler` | `reason` |
| `GetHistory` | `HistoryHandler` | None |

Query and bulk-purge calls have no target instance ID. SDK overloads delegate to one recorded
operation, including both instance lookup aliases. Management handlers receive original arguments
and cancellation tokens, return their configured results, and propagate failures unchanged. Restart
results must be non-empty. Apart from scheduling and metadata lookup defaults, handlers must be
configured; missing handlers throw `InvalidOperationException` naming the required property.
Management calls do not change seeded metadata, execute history, or enforce backend policies.

## Build instance metadata

`TestOrchestrationMetadataBuilder` creates SDK `OrchestrationMetadata` values with a pending status,
Unix epoch timestamps, and the SDK JSON converter. Configure status, input, output, custom status,
tags, failure details, and timestamps as needed:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableMetadataTests.cs#docs-functions-durable-metadata -->
```csharp
[Fact]
public async Task Seeded_metadata_supports_status_endpoints_and_typed_payload_reads()
{
    var metadata = new TestOrchestrationMetadataBuilder("Order", "order-42")
        .WithRuntimeStatus(OrchestrationRuntimeStatus.Completed)
        .WithInput(new OrderInput(42))
        .WithOutput("accepted")
        .WithCustomStatus(new { Step = "finished" })
        .Build();
    await using var client = new TestDurableTaskClient().WithInstance(metadata);
    var token = TestContext.Current.CancellationToken;

    var summary = await client.GetInstanceAsync("order-42", token);
    Assert.Equal(OrchestrationRuntimeStatus.Completed, summary!.RuntimeStatus);
    Assert.Null(summary.SerializedInput);
    Assert.Throws<InvalidOperationException>(() => summary.ReadInputAs<OrderInput>());

    var details = await client.GetInstanceAsync("order-42", getInputsAndOutputs: true, cancellation: token);
    Assert.Equal(new OrderInput(42), details!.ReadInputAs<OrderInput>());
    Assert.Equal("accepted", details.ReadOutputAs<string>());
    Assert.Equal("finished", details.ReadCustomStatusAs<JsonElement>().GetProperty("Step").GetString());
    client.Should().HaveOperation(DurableClientOperation.GetInstance, "order-42", times: 2);
}
```
<!-- end-snippet -->

`WithFailure(exception)` captures SDK failure details, including inner exceptions, and sets Failed
status. `WithFailure(details)` accepts an existing descriptor. Choosing a non-failed status clears
failure details. `WithTimestamps(createdAt, lastUpdatedAt)` normalizes to UTC and rejects updates
before creation. `WithTags(tags)` copies tags. `WithDataConverter(converter)` customizes payload
serialization and typed readers; the converter remains caller-owned.

Payloads are serialized at `Build()` time, so later input changes affect future builds but cannot
change an already built payload. Each build returns fresh metadata and tag containers. Failure
descriptors and the converter are retained by reference.

`client.WithInstance(metadata)` seeds or replaces one fixture using case-sensitive instance IDs.
It copies scalar values and tags. Lookups return fresh snapshots: without `getInputsAndOutputs`,
serialized payloads, failure details, and the converter are omitted. A configured
`GetInstanceHandler` overrides fixture lookup. Queries and waits still use their explicit handlers;
fixtures do not implement runtime state transitions, polling, or automatic scheduling.

## Assert calls and capture diagnostics

`client.Should()` provides fluent checks over recorded attempts:

```csharp
client.Should()
    .HaveScheduledOrchestration("Order", "order-42")
    .HaveRaisedEvent("order-42", "Approved")
    .HaveOperation(DurableClientOperation.Terminate, "order-42", times: 0);
```

Counts are exact and default to one; zero asserts absence. Orchestration names and instance IDs
use ordinal case-sensitive comparison; event names are case-insensitive. `HaveCount` counts all
operations, including reads. `BeEmpty` requires no calls. `HaveOperationSequence` checks the whole
ordered sequence. `HaveMatchingOperations(predicate, times)` supports argument checks through
`GetArgument<T>`; predicate exceptions propagate. A failed check throws
`TestDurableTaskClientVerificationException` with the expectation and a timeline of operation names,
instance IDs, and orchestration/event targets, excluding payload values and handler failure text.

The client implements `ITestScenarioResource`. Register it with `UseScenarioResource` to attach its
timeline to scenario failures and reset state before and after each scenario:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableClientManagementTests.cs#docs-functions-durable-client-diagnostics -->
```csharp
[Fact]
public async Task Scenario_failure_attaches_timeline_before_resetting_client_state()
{
    await using var client = new TestDurableTaskClient
    {
        RaiseEventHandler = (_, _, _, _) => Task.CompletedTask
    };
    await using var host = AzureFunctionTestHost.CreateBuilder()
        .UseScenarioResource("Durable", client)
        .Build();
    var error = await Assert.ThrowsAsync<TestDurableTaskClientVerificationException>(() =>
        host.RunInTestScenarioScopeAsync(async (_, token) =>
        {
            client.WithInstance(new TestOrchestrationMetadataBuilder("Order", "order-42").Build());
            await client.RaiseEventAsync("order-42", "Approved", new { Secret = "private-payload" }, token);
            client.Should().HaveRaisedEvent("order-42", "Rejected");
        }, TestContext.Current.CancellationToken));

    var diagnostics = Assert.IsType<TestScenarioDiagnostics>(error.Data[TestScenarioDiagnostics.ExceptionDataKey]);
    var json = JsonSerializer.Serialize(diagnostics.Resources["Durable"]);
    Assert.Contains("RaiseEvent", json);
    Assert.Contains("Approved", json);
    Assert.Contains("order-42", json);
    Assert.DoesNotContain("private-payload", json);
    Assert.Contains("Recorded attempts:", error.Message);
    client.Should().BeEmpty();
    Assert.Null(await client.GetInstanceAsync("order-42", TestContext.Current.CancellationToken));
}
```
<!-- end-snippet -->

`CaptureDiagnosticsAsync` returns a detached serializable snapshot of the timeline and seeded
instance names, IDs, and statuses. Payloads, options, reasons, history contents, and failure details
are excluded. Names and IDs are included without automatic redaction. This also allows capture when
an original payload cannot be serialized. Scenario diagnostics are attached before cleanup.

`ResetAsync` clears recordings, fixtures, and both ID/operation sequences, preserving configured
handlers. Arrange fixtures inside the scenario after its initial reset. Await in-flight calls before
resetting; reset does not cancel handlers or external side effects. A pre-canceled reset leaves state
unchanged. The host borrows the registered resource; the caller retains disposal ownership.

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
| `Entities` | Calls return `default(T)`, signals complete, lock scopes maintain local critical-section markers | `WithEntities(new TestOrchestrationEntityFeature())` adds recording and configured calls |
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

Use the [entity helpers](azure-functions-durable-entities.md) for recorded calls/signals and explicit
execution of real entity implementations against test-controlled state.

## Upgrade to 1.3.0

The existing activity-handler constructor remains supported. Context operations that previously
threw `NotSupportedException` now use the documented in-memory defaults. Tests that asserted those
exceptions should instead assert the recorded operation or configure the corresponding `With...`
handler to model the failure they need.

Timers and event waits remain pending until the test advances time, raises an event, or cancels the
wait. Activity, entity, and child stubs do not run application implementations automatically; register
handlers when their results matter. `ContinueAsNew` records a request without restarting the
orchestrator. The [orchestration recipes](azure-functions-durable-recipes.md) show explicit dispatch
and continuation across separate test contexts.

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
