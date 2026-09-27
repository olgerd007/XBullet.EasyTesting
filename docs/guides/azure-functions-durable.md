# Test Durable activity dispatch

`TestOrchestrationContext` supports a focused unit-test boundary for .NET isolated Durable
orchestrators: it records and dispatches `CallActivityAsync` calls. Use it when orchestration logic
primarily chooses an activity name, input, options, and expected result type.

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

## Supported and unsupported behavior

Only `CallActivityAsync` is emulated. Runtime-dependent context members fail explicitly:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableFunctionTests.cs#docs-functions-durable-limitations -->
```csharp
[Fact]
public void Runtime_dependent_members_are_not_supported()
{
    var context = new TestOrchestrationContext(_ => Task.FromResult<object?>(null));

    Assert.Throws<NotSupportedException>(() => _ = context.InstanceId);
    Assert.Throws<NotSupportedException>(() => context.GetInput<string>());
    Assert.Throws<NotSupportedException>(() => context.NewGuid());
}
```
<!-- end-snippet -->

The test context does not emulate:

- Durable history or deterministic replay.
- Timers or orchestration time.
- External events.
- Sub-orchestrators or child-instance lifecycle.
- Entity calls, suspension, termination, purge, or instance management.
- Runtime-generated instance IDs or deterministic GUIDs.

Those behaviors require the real Durable runtime or a dedicated runtime-level harness. Do not use
this helper to claim replay safety or production scheduling correctness.

## Troubleshooting dispatch

- An unexpected activity should make the handler throw with the recorded activity name.
- A null result for a value type or a mismatched result type produces an explicit dispatch error.
- If orchestrator code reads runtime-dependent members, `NotSupportedException` identifies that the
  scenario has crossed this helper's boundary.

See [`DurableFunctionTests`](../../tests/TestFunctions.IntegrationTests/DurableFunctionTests.cs) for
handler validation and result-type failure coverage.

See the [Azure Functions API reference](../api/packages/xbullet-easytesting-azurefunctions.md) for
`TestOrchestrationContext` and `RecordedActivityCall`.
