# Connect Durable starters, orchestrators, and activities

`TestDurableWorkflow` connects a `TestDurableTaskClient` to named orchestrators and activities using
an existing `AzureFunctionTestHost`. It executes real function delegates through worker middleware,
with a separate DI invocation scope for each orchestrator and activity. Input, activity results,
child results, and events use direct assignment by default. `WithSerialization()` enables SDK
converter snapshots and deserialization at payload boundaries.

This is an opt-in application-workflow harness. Standalone
[`TestDurableTaskClient`](azure-functions-durable.md#test-http-starters) and
[`TestOrchestrationContext`](azure-functions-durable.md#dispatch-to-a-real-activity) retain their
existing recording and manually configured dispatch behavior.

## Register and run a workflow

Register function classes and dependencies in the host, attach it with `UseHost`, then register
named delegates. `AddOrchestrator<TFunction, TResult>` receives the resolved function, orchestration
context, and worker context. `AddActivity<TFunction, TInput, TResult>` receives the resolved function,
typed input, and worker context. Use `worker.CancellationToken` in application methods that accept a
token. Overloads without `TResult` support functions returning `Task`.

This executable example invokes an HTTP starter, runs two activities, then supplies approval:

<!-- snippet: tests/TestFunctions.IntegrationTests/ConnectedDurableWorkflowTests.cs#docs-functions-connected-workflow -->
```csharp
[Fact]
public async Task Http_starter_runs_real_orchestrator_and_activities_then_accepts_approval()
{
    var signals = new ExecutionSignals();
    await using var host = AzureFunctionTestHost.CreateBuilder()
        .AddFunction<StartOrderOrchestrationFunction>()
        .AddFunction<OrderOrchestrator>()
        .AddFunction<PriceActivity>()
        .UseMiddleware<ScopeMiddleware>()
        .ConfigureServices(services =>
        {
            services.AddSingleton(signals);
            services.AddScoped<ScopeMarker>();
        })
        .Build();
    await using var workflow = new TestDurableWorkflow().UseHost(host)
        .AddOrchestrator<OrderOrchestrator, string>("ProcessOrder", (function, context, worker) => function.RunAsync(context, worker))
        .AddActivity<PriceActivity, string, string>("Price", (function, input, worker) => function.RunAsync(input, worker));
    var token = TestContext.Current.CancellationToken;
    var request = host.HttpRequest(nameof(StartOrderOrchestrationFunction), token)
        .WithMethod(HttpMethod.Post).WithTextBody("order input").Build();

    var invocation = await host.InvokeAsync<StartOrderOrchestrationFunction, HttpResponseData>(
        (TestFunctionContext)request.FunctionContext,
        (function, context) => function.RunAsync(request, workflow.Client, context.CancellationToken));
    Assert.Equal(HttpStatusCode.Accepted, invocation.Result!.StatusCode);
    await signals.Ready.Task.WaitAsync(Timeout, token);
    var result = workflow.GetResultAsync<string>("order-42", token);
    Assert.False(result.IsCompleted);
    await workflow.Client.RaiseEventAsync("order-42", "Approved", true, token);

    Assert.Equal("priced: priced: order input", await result.WaitAsync(Timeout, token));
    var metadata = await workflow.Client.WaitForInstanceCompletionAsync("order-42", true, token);
    Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
    Assert.Equal("awaiting approval", metadata.ReadCustomStatusAs<string>());
    Assert.Equal("order input", metadata.ReadInputAs<string>());
    workflow.Client.Should().HaveScheduledOrchestration("ProcessOrder", "order-42").HaveRaisedEvent("order-42", "Approved");
    Assert.Equal(2, workflow.GetContext("order-42").ActivityCalls.Count);
    Assert.Equal(4, signals.MiddlewareScopes.Count);
    Assert.Equal(4, signals.MiddlewareScopes.Select(item => item.Scope).Distinct().Count());
    Assert.All(signals.MiddlewareScopes, item => Assert.True(item.Scope.Disposed));
    Assert.All(signals.FunctionScopes, item => Assert.Contains(signals.MiddlewareScopes,
        middleware => middleware.InvocationId == item.InvocationId && middleware.Scope == item.Scope));
}
```
<!-- end-snippet -->

See the [test source](../../tests/TestFunctions.IntegrationTests/ConnectedDurableWorkflowTests.cs)
for the orchestrator, activity, and middleware implementations used in this example.

Function classes must already be registered in the borrowed host's DI container. Task names and
instance IDs use ordinal case-sensitive matching. Duplicate names within each function category
are rejected. Registrations freeze at the first successful schedule and become editable after reset;
reset retains existing registrations. Middleware that short-circuits a connected invocation produces
an explicit failure, including for functions whose legitimate result could otherwise be null.

Scheduling returns an instance ID without awaiting execution. Supplied IDs are honored; generated
root IDs follow the client's `test-instance-N` sequence. Reusing any existing ID, including a
completed instance ID, fails until reset. A scheduling cancellation token only affects the schedule
attempt. It does not become the execution's lifetime token.

`GetResultAsync<TResult>(id, cancellationToken)` awaits the result after the orchestrator's scope
has closed, validates assignability (or deserializes to the requested type), and propagates the
original execution failure. Canceling that
wait does not cancel the workflow. Give pending event/timer workflows an explicit test timeout.
The client also supports `WaitForInstanceStartAsync` and `WaitForInstanceCompletionAsync`; these
return metadata, including Failed metadata when execution fails. Started means dispatch has begun,
and does not guarantee that application code has reached a particular activity or wait.

## Enable serialization compatibility checks

Call `WithSerialization()` before scheduling to use `JsonDataConverter.Default`, or pass the same
SDK `DataConverter` used by your application. The workflow does not discover a converter from host DI.
The default mode continues to use direct assignment, including shared object references.

<!-- snippet: tests/TestFunctions.IntegrationTests/ConnectedDurableWorkflowSerializationTests.cs#docs-functions-connected-serialization -->
```csharp
[Fact]
public async Task Serialization_converts_payload_shapes_and_returns_independent_results()
{
    await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
    await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
        .AddOrchestrator<WorkflowFunction, Payload>("Order", async (_, context, _) =>
        {
            var input = context.GetInput<Payload>();
            return await context.CallActivityAsync<Payload>("Increment", new { input.Value });
        })
        .AddActivity<WorkflowFunction, Payload, object>("Increment", (_, input, _) =>
            Task.FromResult<object>(new { Value = input.Value + 1 }));
    var token = TestContext.Current.CancellationToken;
    var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", new { Value = 2 }, token);

    var result = await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token);
    Assert.Equal(3, result.Value);
    result.Value = 99;
    Assert.Equal(3, (await workflow.GetResultAsync<Payload>(id, token)).Value);
    var metadata = await workflow.Client.GetInstanceAsync(id, true, token);
    Assert.Equal(2, metadata!.ReadInputAs<Payload>()!.Value);
    Assert.Equal(3, metadata.ReadOutputAs<Payload>()!.Value);
}

public sealed class Payload
{
    public int Value { get; set; }
}

public sealed class WorkflowFunction;
```
<!-- end-snippet -->

With serialization enabled, anonymous objects or JSON-compatible shapes can cross boundaries into
typed DTOs. Mutation of a received object does not change the sender's object or later result reads.
The converter owns property naming, polymorphism, enum formats, and supported types. Requests for
`object` follow that converter's behavior; the SDK JSON converter normally returns `JsonElement`
for JSON objects rather than reconstructing the original CLR type.

| Boundary | Snapshot timing | Deserialization target |
| --- | --- | --- |
| Root or child input | Before scheduling returns or child execution begins | Each `GetInput<T>()` request |
| Activity input | At activity dispatch | Registered `TInput`, also supplied to worker input bindings |
| Activity or child result | After the producer's invocation pipeline and scope finish | Caller's requested `TResult` |
| Root result | After the orchestrator's invocation pipeline and scope finish | Each `GetResultAsync<TResult>()` request |
| External or sent event | Before enqueuing to the receiving context | Waiting `WaitForExternalEvent<T>()` request |
| Custom status | On `SetCustomStatus` | Metadata's typed status reader |

Client and context recordings retain original arguments for assertions. `CustomStatus` also retains
its original reference; metadata uses the serialized status snapshot. Metadata reuses captured
payloads and the configured converter without wrapping or serializing those snapshots again.
Reads excluding payloads do not invoke the converter. Absent orchestration input still yields
`default(T)`; void results remain null and require nullable/reference result readers.

Input serialization failures fail the submitting operation. A failed root schedule leaves no
instance; failed activity or child submissions fault the calling orchestrator. Failed event
serialization leaves the receiving wait intact. Deserialization failures fault the receiving
call or wait; a failed final result read does not change an already completed instance's status.
Output serialization failures fail the producing invocation's workflow operation. Original converter
exceptions propagate through result waits, while status-only metadata and diagnostics omit payloads
and failure contents. Events to terminal instances remain ignored without payload conversion.

Configuration freezes at the first successful schedule. Reset retains the mode, converter, and
registrations, and allows converter reconfiguration after cleanup succeeds. Configure before
concurrent use; caller-owned custom converters must support concurrent calls. Serialization does
not enable retries, history, replay, persistence, or real worker trigger bindings. Entity helpers
retain their own explicit converter configuration.

## Retry and failure-sequence tests

`WithRetries()` enables bounded execution of SDK retry policies and custom retry handlers for
connected activities and children. Delays use the calling context's manual clock. `Attempts`
records each invocation while context call collections retain one entry per logical call.
`TestDurableTaskSequence<TResult>` supplies configured failures and results to delegates.
See the [retry guide](azure-functions-durable-retries.md) for timing, limits, filters, and cleanup.

## Metadata, events, children, and time

- Client metadata reflects this harness's Pending, Running, Completed, and Failed execution states.
  Payloads and failure details are included only when requested. In default mode, the SDK JSON
  converter serializes metadata payloads while execution values pass by reference; metadata reads
  can fail serialization for otherwise valid in-memory values. Serialized workflows use their
  configured converter and captured payloads.
- `Client.RaiseEventAsync(id, name, payload, token)` routes to that instance's context, buffering
  events before a wait. Event names are case-insensitive. `context.SendEvent(...)` uses the same
  routing, including self-delivery exactly once. Unknown IDs fail explicitly; events sent to known
  terminal instances are ignored. A malformed event payload faults the receiving wait.
- `CallSubOrchestratorAsync` dispatches registered children using separate contexts and scopes.
  `SubOrchestrationOptions.InstanceId` and `Version` are applied, and parent metadata is set.
  Generated child IDs use `parent-id:child:N`, with a workflow-wide sequence reset between scenarios.
  Activity and child calls remain recorded on their originating context.
- `GetContext(id)` exposes manual time advancement and recorded calls. Each clock begins at Unix
  epoch, or the parent's current time for a newly created child. Advancing a child does not advance
  its parent. Timers and their continuations behave as documented for `TestOrchestrationContext`.
  Do not change a connected context's identity or replace its dispatch configuration while it runs.

## DI and scenario integration

Create the workflow before building a host when its client or scenario resource must be registered.
Then call `UseHost` exactly once and dispose the workflow before the host:

<!-- snippet: tests/TestFunctions.IntegrationTests/ConnectedDurableWorkflowLifetimeTests.cs#docs-functions-connected-scenario -->
```csharp
[Fact]
public async Task Scenario_failure_captures_workflow_before_canceling_and_draining_its_scopes()
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ConnectedDurableWorkflowTests.ScopeMarker? marker = null;
    var workflow = new TestDurableWorkflow()
        .AddOrchestrator<WaitingFunction, string>("Approval", async (function, context, _) =>
        {
            marker = function.Scope;
            var waiting = context.WaitForExternalEvent<string>("Approved");
            entered.SetResult();
            return await waiting;
        });
    await using var host = AzureFunctionTestHost.CreateBuilder()
        .AddFunction<WaitingFunction>()
        .ConfigureServices(services => services.AddScoped<ConnectedDurableWorkflowTests.ScopeMarker>())
        .UseScenarioResource("Workflow", workflow)
        .Build();
    await using var connected = workflow.UseHost(host);
    var token = TestContext.Current.CancellationToken;
    var failure = new InvalidOperationException("test assertion failed");

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunInTestScenarioScopeAsync(async (_, cancellation) =>
    {
        await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Approval", cancellation);
        await entered.Task.WaitAsync(Timeout, cancellation);
        Assert.False(marker!.Disposed);
        throw failure;
    }, token));

    Assert.Same(failure, error);
    Assert.True(marker!.Disposed);
    var diagnostics = Assert.IsType<TestScenarioDiagnostics>(error.Data[TestScenarioDiagnostics.ExceptionDataKey]);
    var json = JsonSerializer.Serialize(diagnostics.Resources["Workflow"]);
    Assert.Contains("Approval", json);
    Assert.Contains("Running", json);
    workflow.Client.Should().BeEmpty();
    Assert.Null(await workflow.Client.GetInstanceAsync("test-instance-1", token));
}
```
<!-- end-snippet -->

For a constructor-injected starter, register `workflow.Client` as `DurableTaskClient` while building
the host. Alternatively pass an existing `TestDurableTaskClient` to the workflow constructor and
register that client in DI. The supplied client's schedule, lookup, event, and start/completion wait
handlers must be unset. The workflow wires those handlers for its lifetime; do not replace them or
reset the client independently. Seeded client fixtures are superseded by connected instance lookup.
Other client management/query handlers remain explicitly configurable and do not implement workflow
lifecycle changes automatically.

Register the **workflow** with `UseScenarioResource`, which resets it before and after scenarios and
captures diagnostics before failure cleanup. Do not also register its client separately for reset.
Diagnostics include client operations, active activity count, and instance names, IDs, statuses,
parent IDs, and dispatch counts. Payloads, outputs, custom status, and exception details are excluded.
Names and IDs are included without automatic redaction. The host borrows the resource; the caller
owns workflow disposal.

## Cleanup and supported boundaries

`ResetAsync(token)` cancels execution and drains orchestrators, children, and in-flight activities,
including activities still running after an orchestrator returns. It then clears instances, client
recordings/fixtures, and generated sequences while retaining registrations and handlers. A reset
that is canceled before starting leaves execution intact. Once reset begins, cancellation of
executions cannot be rolled back, even if cleanup subsequently times out or its wait is canceled.

Connected event and timer waits also observe workflow cancellation when application code omitted
a token. Activities and other user I/O must honor their worker token. Cancellation due to cleanup
is represented as Terminated in harness metadata; this does not reproduce Durable termination.
Completed instances retain their results if unrelated work prevents cleanup from completing.

The default cleanup deadline is five wall-clock seconds, configurable through the constructor's
positive finite `shutdownTimeout`. A timeout retains state and diagnostic access without disposing
live invocation scopes. Release uncooperative work and retry reset or disposal while keeping the
host alive. Concurrent cleanup calls are rejected. Successful disposal is idempotent, closes the
workflow to new use, and leaves client recordings readable. The workflow never disposes its host
or a caller-supplied client.

This harness does not implement persistence, replay, entity state, distributed locks, backend
deduplication, or management HTTP endpoints. Retry options are recorded without execution unless
`WithRetries` is enabled. `StartAt` scheduling is rejected. `ContinueAsNew` is recorded and fails the instance
explicitly; use separate contexts for continuation tests. Function binding parameters are supplied
by the registered delegates; the host does not automatically discover functions or bind parameters.

See the [Durable boundary guide](azure-functions-durable.md) and
[manual orchestration recipes](azure-functions-durable-recipes.md) for complementary tests.
The [entity helpers](azure-functions-durable-entities.md) support explicit entity dispatch through
an orchestrator's configured entity feature; entity execution is not connected automatically.
