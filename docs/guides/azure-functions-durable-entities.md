# Durable Entity tests

Entity helpers provide three explicit boundaries for isolated-worker tests. They use the SDK
abstractions already referenced by `XBullet.EasyTesting.AzureFunctions`; no emulator is required.

| Test boundary | Helper | Behavior |
| --- | --- | --- |
| Orchestrator calls and signals | `TestOrchestrationEntityFeature` | Records attempts and supplies configured results |
| Starter or HTTP client | `TestDurableTaskClient.Entities` / `TestDurableEntityClient` | Records signals, returns metadata fixtures, delegates queries and cleanup |
| Entity implementation | `TestEntityOperation`, `TestEntityContext`, `TestEntityState` | Runs real `ITaskEntity` logic against test-controlled state |

## Execute real entity operations

Share a context and state across sequential operations for one entity. Invoke the SDK's
`ITaskEntity.RunAsync` directly, including when your class inherits `TaskEntity<TState>`.
The SDK owns method dispatch, parameter binding, async results, initialization, and implicit deletion.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableEntityTests.cs#docs-functions-entity-dispatch -->
```csharp
[Fact]
public async Task Real_entity_dispatcher_persists_state_across_operations_and_deletes_it()
{
    var id = new EntityInstanceId("Counter", "order-42");
    var context = new TestEntityContext(id);
    var state = new TestEntityState();
    ITaskEntity counter = new CounterEntity();

    await counter.RunAsync(new TestEntityOperation(context, "Add", state, 3));
    await counter.RunAsync(new TestEntityOperation(context, "Add", state, 4));
    var result = await counter.RunAsync(new TestEntityOperation(context, "Get", state));

    Assert.Equal(7, Assert.IsType<int>(result));
    Assert.Equal(7, state.GetState<int>());
    await counter.RunAsync(new TestEntityOperation(context, "delete", state));
    Assert.False(state.HasState);
}

public sealed class CounterEntity : TaskEntity<int>
{
    public void Add(int amount) => State += amount;
    public int Get() => State;
}
```
<!-- end-snippet -->

`TestEntityState` serializes its initial value and every `SetState` call. Each read deserializes a
fresh value, so mutating a returned object does not save it. Null deletes state; absent state
returns null from the non-generic reader and the supplied default from the generic reader.
Use a separate state object for each entity ID. The helper does not associate state with IDs for you.

`TestEntityOperation` snapshots its input at construction; null means no input. Both state and
input support an optional SDK `DataConverter`, defaulting to `JsonDataConverter.Default`.
`TestEntityContext.Signals` and `ScheduledOrchestrations` record outgoing operations. Configure
its synchronous `SignalHandler` or `ScheduleHandler` to model submission failures or routing.
Scheduling returns an explicit ID or `test-entity-instance-N`; it never starts an orchestration.

## Configure orchestrator interactions

Attach a recording feature with `WithEntities(feature)`. `CallHandler` is required for result and
void calls; missing configuration fails clearly after recording the attempt. Typed results use
assignment compatibility, without automatic JSON conversion. Null is valid only for nullable or
reference results. Handler failures propagate and remain recorded.

The handler can return a fixture, fail an operation, or explicitly dispatch real entity code:

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableEntityTests.cs#docs-functions-entity-feature -->
```csharp
[Fact]
public async Task Orchestrator_entity_calls_can_explicitly_dispatch_real_entity_code()
{
    var id = new EntityInstanceId("Counter", "order-42");
    var state = new TestEntityState();
    var entityContext = new TestEntityContext(id);
    var feature = new TestOrchestrationEntityFeature
    {
        CallHandler = async call => await new CounterEntity().RunAsync(
            new TestEntityOperation(entityContext, call.OperationName!, state, call.Input))
    };
    var orchestration = new TestOrchestrationContext().WithEntities(feature);

    await orchestration.Entities.CallEntityAsync(id, "Add", 5);
    Assert.Equal(5, await orchestration.Entities.CallEntityAsync<int>(id, "Get"));
    await orchestration.Entities.SignalEntityAsync(id, "Add", 10);

    // Signals record submission; the test decides when to deliver them.
    Assert.Equal(5, state.GetState<int>());
    Assert.Equal(new[] { EntityOperationKind.Call, EntityOperationKind.Call, EntityOperationKind.Signal },
        feature.Operations.Select(call => call.Kind));
    Assert.Equal(id, feature.Operations[2].Id);
}
```
<!-- end-snippet -->

Signals record submission and complete without delivery unless `SignalHandler` is configured.
Inputs and SDK options in recordings retain original references. `LockEntitiesAsync` records
distinct entity IDs and maintains local `InCriticalSection` markers until each scope is disposed.
These markers provide no distributed mutual exclusion or production lock validation.

The parameterless `TestOrchestrationContext` keeps its existing default entity stubs. Attaching
the recording feature is explicit. Connected workflow contexts can be configured inside their
registered orchestrator delegate; the workflow does not route entity operations automatically.

## Test entity clients

`TestDurableTaskClient.Entities` exposes a stable `TestDurableEntityClient`, including through
the SDK's `DurableTaskClient` base type. It also works as a standalone client boundary.

<!-- snippet: tests/TestFunctions.IntegrationTests/DurableEntityTests.cs#docs-functions-entity-client -->
```csharp
[Fact]
public async Task Client_entities_support_signals_and_typed_metadata_fixtures()
{
    var id = new EntityInstanceId("Counter", "order-42");
    await using var client = new TestDurableTaskClient();
    client.Entities.WithEntity(new EntityMetadata<int>(id, 12)
    {
        LastModifiedTime = DateTimeOffset.UnixEpoch,
        BacklogQueueSize = 2
    });
    DurableTaskClient sdk = client;
    var token = TestContext.Current.CancellationToken;

    await sdk.Entities.SignalEntityAsync(id, "Add", 3, cancellation: token);
    var metadata = await sdk.Entities.GetEntityAsync<int>(id, cancellation: token);

    Assert.NotNull(metadata);
    Assert.Equal(12, metadata.State);
    Assert.Equal(2, metadata.BacklogQueueSize);
    Assert.Equal(EntityOperationKind.Signal, client.Entities.Operations[0].Kind);
    Assert.Equal(3, client.Entities.Operations[0].Input);
    // A signal does not mutate seeded metadata.
    Assert.Equal(12, (await sdk.Entities.GetEntityAsync<int>(id, cancellation: token))!.State);
}
```
<!-- end-snippet -->

`WithEntity` copies metadata and snapshots typed state. Serialized `EntityMetadata` fixtures
retain their immutable `SerializedData` wrapper and converter. Missing IDs return null, and
`includeState: false` produces metadata whose state is excluded. `GetEntityHandler` overrides
fixtures; projection still honors state exclusion. Signals never update fixtures automatically.

`QueryHandler` supplies SDK `AsyncPageable<EntityMetadata>` pages for both raw and typed queries.
Typed queries deserialize included states with each fixture's converter. Projection preserves
page boundaries, continuation tokens, metadata, and enumeration cancellation. The handler owns
filtering, ordering, and page retrieval; projection honors `IncludeState`. `CleanHandler` receives
the original cleanup request, completion flag, and caller token. Query and cleanup operations
fail clearly without handlers. All attempts appear in `Operations`; invalid arguments and
pre-canceled calls are excluded. Ordinary handler failures remain recorded.

## Scenarios and limits

The entity feature and entity client implement `ITestScenarioResource`. Reset clears recordings,
local lock markers, and client metadata fixtures while preserving handlers. Await all in-flight
operations before resetting; reset does not cancel user callbacks. Arrange fixtures inside the
scenario after its initial reset. Diagnostics include IDs, operation names, kinds, and ordering,
excluding state, inputs, results, options, and failure contents.

Register a parent `TestDurableTaskClient` as the scenario resource to reset its entity client
and include entity diagnostics automatically. Entity attempts have their own `Entities.Operations`
sequence, separate from orchestration `Operations`. When using `TestDurableWorkflow`, register
the workflow as the resource as described in the [connected workflow guide](azure-functions-durable-workflows.md).
Custom entity features and state objects remain owned by their tests.

These helpers do not implement entity-trigger worker binding, durable storage, signal delivery,
scheduled delivery, operation batching, retries, replay, distributed locks, or transactional
rollback. A callback can route a signal explicitly, but its completion means whatever that callback
implements. State writes and outgoing operations are not rolled back together on failure.
Use the real Durable runtime for those guarantees.
