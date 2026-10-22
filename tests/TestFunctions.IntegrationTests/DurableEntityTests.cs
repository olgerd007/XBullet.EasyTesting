using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableEntityTests
{
    private static readonly EntityInstanceId CounterId = new("Counter", "order-42");

    #region docs-functions-entity-dispatch

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

    #endregion

    #region docs-functions-entity-feature

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

    #endregion

    #region docs-functions-entity-client

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

    #endregion

    [Fact]
    public async Task Call_handlers_record_failures_and_require_assignable_results()
    {
        var feature = new TestOrchestrationEntityFeature();
        await Assert.ThrowsAsync<InvalidOperationException>(() => feature.CallEntityAsync<int>(CounterId, "Get"));
        feature.CallHandler = _ => Task.FromResult<object?>("wrong type");
        await Assert.ThrowsAsync<InvalidOperationException>(() => feature.CallEntityAsync<int>(CounterId, "Get"));
        feature.CallHandler = _ => Task.FromResult<object?>(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => feature.CallEntityAsync<int>(CounterId, "Get"));
        Assert.Null(await feature.CallEntityAsync<string?>(CounterId, "Get"));
        await feature.CallEntityAsync(CounterId, "Void");
        var failure = new InvalidOperationException("entity failure");
        feature.CallHandler = _ => Task.FromException<object?>(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => feature.CallEntityAsync<int>(CounterId, "Get")));
        Assert.Equal(6, feature.Operations.Count);
        Assert.Equal(Enumerable.Range(1, 6).Select(index => (long)index), feature.Operations.Select(call => call.Sequence));
    }

    [Fact]
    public async Task Signals_preserve_original_input_options_and_handler_failures()
    {
        var feature = new TestOrchestrationEntityFeature();
        TaskOrchestrationEntityFeature sdk = feature;
        var input = new object();
        var options = new SignalEntityOptions();
        var failure = new InvalidOperationException("submission failed");
        feature.SignalHandler = _ => Task.FromException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => sdk.SignalEntityAsync(CounterId, "Add", input, options)));
        var call = Assert.Single(feature.Operations);
        Assert.Same(input, call.Input);
        Assert.Same(options, call.Options);
        feature.SignalHandler = null;
        await sdk.SignalEntityAsync(CounterId, "Add", options);
        Assert.Null(feature.Operations[1].Input);
        Assert.Same(options, feature.Operations[1].Options);
    }

    [Fact]
    public async Task Local_lock_scopes_are_distinct_nestable_and_idempotently_disposed()
    {
        var feature = new TestOrchestrationEntityFeature();
        var other = new EntityInstanceId("Counter", "other");
        var outer = await feature.LockEntitiesAsync([CounterId, CounterId]);
        var inner = await feature.LockEntitiesAsync([CounterId, other]);
        Assert.True(feature.InCriticalSection(out var ids));
        Assert.Equal(new[] { CounterId, other }, ids);
        await outer.DisposeAsync();
        await outer.DisposeAsync();
        Assert.True(feature.InCriticalSection(out ids));
        Assert.Equal(2, ids.Count);
        await feature.ResetAsync(TestContext.Current.CancellationToken);
        var current = await feature.LockEntitiesAsync([other]);
        await inner.DisposeAsync();
        Assert.True(feature.InCriticalSection(out ids));
        Assert.Equal(other, Assert.Single(ids));
        await current.DisposeAsync();
        Assert.False(feature.InCriticalSection(out ids));
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Feature_reset_keeps_handlers_and_diagnostics_exclude_payloads()
    {
        var feature = new TestOrchestrationEntityFeature { CallHandler = _ => Task.FromResult<object?>(8) };
        await feature.SignalEntityAsync(CounterId, "Add", "secret-payload");
        var snapshot = await feature.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.Contains("order-42", json);
        Assert.Contains("Signal", json);
        Assert.DoesNotContain("secret-payload", json);
        await feature.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Empty(feature.Operations);
        Assert.Equal(8, await feature.CallEntityAsync<int>(CounterId, "Get"));
        Assert.Equal(1, Assert.Single(feature.Operations).Sequence);
        Assert.Equal(json, JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task Entity_client_overloads_forward_once_and_propagate_cancellation()
    {
        var client = new TestDurableEntityClient();
        Microsoft.DurableTask.Client.Entities.DurableEntityClient sdk = client;
        var token = TestContext.Current.CancellationToken;
        var options = new SignalEntityOptions();
        client.SignalHandler = (call, cancellation) =>
        {
            Assert.Equal(token, cancellation);
            Assert.Equal(EntityOperationKind.Signal, call.Kind);
            return Task.CompletedTask;
        };
        await sdk.SignalEntityAsync(CounterId, "Add", token);
        await sdk.SignalEntityAsync(CounterId, "Add", options, token);
        Assert.Equal(2, client.Operations.Count);
        Assert.Same(options, client.Operations[1].Options);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sdk.SignalEntityAsync(CounterId, "Add", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sdk.GetEntityAsync(CounterId, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sdk.GetEntityAsync<int>(CounterId, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sdk.CleanEntityStorageAsync(cancellation: canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResetAsync(canceled.Token).AsTask());
        Assert.Equal(2, client.Operations.Count);
    }

    [Fact]
    public async Task Metadata_reads_copy_fixtures_and_respect_absent_and_excluded_state()
    {
        var client = new TestDurableEntityClient();
        var original = new CounterState { Value = 7 };
        client.WithEntity(new EntityMetadata<CounterState>(CounterId, original) { LockedBy = "owner" });
        original.Value = 90;
        var token = TestContext.Current.CancellationToken;
        var metadata = await client.GetEntityAsync<CounterState>(CounterId, token);
        Assert.NotNull(metadata);
        Assert.Equal(7, metadata.State.Value);
        metadata.State.Value = 100;
        Assert.Equal(7, (await client.GetEntityAsync<CounterState>(CounterId, token))!.State.Value);
        var raw = await client.GetEntityAsync(CounterId, token);
        Assert.NotNull(raw);
        Assert.Equal(7, raw.State.ReadAs<CounterState>()!.Value);
        Assert.Equal("owner", raw.LockedBy);
        var excluded = await client.GetEntityAsync<int>(CounterId, includeState: false, token);
        Assert.NotNull(excluded);
        Assert.False(excluded.IncludesState);
        Assert.Throws<InvalidOperationException>(() => excluded.State);
        Assert.False((await client.GetEntityAsync(CounterId, includeState: false, token))!.IncludesState);
        Assert.Null(await client.GetEntityAsync<int>(new EntityInstanceId("Counter", "missing"), token));
        client.WithEntity(new EntityMetadata<int>(CounterId));
        Assert.False((await client.GetEntityAsync<int>(CounterId, token))!.IncludesState);
    }

    [Fact]
    public async Task Entity_queries_preserve_pages_continuations_and_cancellation()
    {
        var client = new TestDurableEntityClient();
        var converter = JsonDataConverter.Default;
        var metadata = new EntityMetadata(CounterId, new SerializedData(converter.Serialize(42)!, converter));
        var filter = new EntityQuery { IncludeState = true };
        var token = TestContext.Current.CancellationToken;
        client.QueryHandler = query =>
        {
            Assert.Same(filter, query);
            return Pageable.Create<EntityMetadata>((continuation, cancellation) =>
            {
                Assert.Equal(token, cancellation);
                Assert.Equal("next", continuation);
                return Task.FromResult(new Page<EntityMetadata>([metadata], "last"));
            });
        };
        await foreach (var page in client.GetAllEntitiesAsync<int>(filter).AsPages("next", 1).WithCancellation(token))
        {
            Assert.Equal("last", page.ContinuationToken);
            Assert.Equal(42, Assert.Single(page.Values).State);
            break;
        }

        Assert.Equal(EntityOperationKind.Query, Assert.Single(client.Operations).Kind);
        Assert.Equal(typeof(int), client.Operations[0].ResultType);
    }

    [Fact]
    public async Task Read_query_and_cleanup_handlers_are_explicit_and_record_attempts()
    {
        var client = new TestDurableEntityClient();
        Assert.Throws<InvalidOperationException>(() => client.GetAllEntitiesAsync());
        Assert.Throws<InvalidOperationException>(() => client.GetAllEntitiesAsync<int>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CleanEntityStorageAsync(cancellation: TestContext.Current.CancellationToken));
        var token = TestContext.Current.CancellationToken;
        client.GetEntityHandler = (id, include, cancellation) =>
        {
            Assert.Equal(CounterId, id);
            Assert.False(include);
            Assert.Equal(token, cancellation);
            return Task.FromResult<EntityMetadata?>(new EntityMetadata(CounterId,
                new SerializedData("99", JsonDataConverter.Default)));
        };
        Assert.False((await client.GetEntityAsync<int>(CounterId, false, token))!.IncludesState);
        var request = new CleanEntityStorageRequest();
        client.CleanHandler = (actual, untilComplete, cancellation) =>
        {
            Assert.Equal(request, actual);
            Assert.False(untilComplete);
            Assert.Equal(token, cancellation);
            return Task.FromResult(new CleanEntityStorageResult());
        };
        await client.CleanEntityStorageAsync(request, false, token);
        Assert.Equal(5, client.Operations.Count);
    }

    [Fact]
    public async Task Parent_client_resets_entities_and_includes_safe_entity_diagnostics()
    {
        await using var client = new TestDurableTaskClient("entity-starter");
        Assert.Equal(client.Name, client.Entities.Name);
        client.Entities.WithEntity(new EntityMetadata<string>(CounterId, "private-state"));
        await client.Entities.SignalEntityAsync(CounterId, "Add", "private-input", cancellation: TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(await client.CaptureDiagnosticsAsync(TestContext.Current.CancellationToken));
        Assert.Contains("order-42", json);
        Assert.DoesNotContain("private-state", json);
        Assert.DoesNotContain("private-input", json);
        await client.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Empty(client.Entities.Operations);
        Assert.Null(await client.Entities.GetEntityAsync(CounterId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Raw_metadata_seeding_preserves_serialized_state_and_excluded_queries_never_deserialize()
    {
        var client = new TestDurableEntityClient();
        var fixture = new EntityMetadata(CounterId, new SerializedData("23", JsonDataConverter.Default))
        {
            BacklogQueueSize = 4,
            LastModifiedTime = DateTimeOffset.UnixEpoch
        };
        client.WithEntity(fixture);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(23, (await client.GetEntityAsync<int>(CounterId, token))!.State);
        var filter = new EntityQuery { IncludeState = false };
        client.QueryHandler = _ => Pageable.Create<EntityMetadata>((_, _) =>
            Task.FromResult(new Page<EntityMetadata>([fixture], null)));
        await foreach (var metadata in client.GetAllEntitiesAsync<CounterState>(filter).WithCancellation(token))
        {
            Assert.False(metadata.IncludesState);
            Assert.Equal(4, metadata.BacklogQueueSize);
        }

        await foreach (var metadata in client.GetAllEntitiesAsync(filter).WithCancellation(token))
        {
            Assert.False(metadata.IncludesState);
            Assert.Equal(DateTimeOffset.UnixEpoch, metadata.LastModifiedTime);
        }
    }

    [Fact]
    public async Task Scenario_failures_attach_entity_timeline_and_reset_fixtures()
    {
        await using var client = new TestDurableTaskClient();
        await using var host = AzureFunctionTestHost.CreateBuilder().UseScenarioResource("Durable", client).Build();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.RunInTestScenarioScopeAsync(async (_, token) =>
            {
                client.Entities.WithEntity(new EntityMetadata<string>(CounterId, "secret-state"));
                await client.Entities.SignalEntityAsync(CounterId, "Add", "secret-input", cancellation: token);
                throw new InvalidOperationException("scenario failed");
            }, TestContext.Current.CancellationToken));

        var diagnostics = Assert.IsType<TestScenarioDiagnostics>(error.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        var json = JsonSerializer.Serialize(diagnostics.Resources["Durable"]);
        Assert.Contains("order-42", json);
        Assert.Contains("Signal", json);
        Assert.DoesNotContain("secret-state", json);
        Assert.DoesNotContain("secret-input", json);
        Assert.Empty(client.Entities.Operations);
        Assert.Null(await client.Entities.GetEntityAsync(CounterId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entity_state_is_independent_for_each_test_owned_instance()
    {
        var first = new TestEntityState(10);
        var second = new TestEntityState(20);
        ITaskEntity counter = new CounterEntity();
        await counter.RunAsync(new TestEntityOperation(new TestEntityContext(CounterId), "Add", first, 2));
        await counter.RunAsync(new TestEntityOperation(new TestEntityContext(new EntityInstanceId("Counter", "other")), "Add", second, 3));
        Assert.Equal(12, first.GetState<int>());
        Assert.Equal(23, second.GetState<int>());
        await Assert.ThrowsAsync<NotSupportedException>(() => counter.RunAsync(
            new TestEntityOperation(new TestEntityContext(CounterId), "Unknown", first)).AsTask());
        Assert.Equal(12, first.GetState<int>());
    }

    [Fact]
    public void Entity_state_reads_and_operation_inputs_are_detached_serialized_values()
    {
        var original = new CounterState { Value = 3 };
        var state = new TestEntityState(original);
        var operation = new TestEntityOperation(new TestEntityContext(CounterId), "Add", state, original);
        original.Value = 20;
        Assert.True(operation.HasInput);
        Assert.Equal(3, operation.GetInput<CounterState>()!.Value);
        var copy = state.GetState<CounterState>()!;
        copy.Value = 30;
        Assert.Equal(3, state.GetState<CounterState>()!.Value);
        state.SetState(copy);
        Assert.Equal(30, state.GetState<CounterState>()!.Value);
        state.SetState(null);
        Assert.Null(state.GetState(typeof(int)));
        Assert.Equal(7, state.GetState(7));
    }

    [Fact]
    public async Task Dispatcher_handles_async_methods_and_context_binding()
    {
        var context = new TestEntityContext(CounterId);
        var state = new TestEntityState();
        ITaskEntity entity = new OutgoingEntity();
        Assert.Equal("test-entity-instance-1", await entity.RunAsync(new TestEntityOperation(context, "Emit", state, 5)));
        Assert.Equal(5, state.GetState<int>());
        var signal = Assert.Single(context.Signals);
        Assert.Equal(CounterId, signal.Id);
        Assert.Equal("Add", signal.OperationName);
        Assert.Equal(5, signal.Input);
        Assert.Equal("FollowUp", Assert.Single(context.ScheduledOrchestrations).OrchestratorName.Name);
        await Assert.ThrowsAsync<NotSupportedException>(() => entity.RunAsync(new TestEntityOperation(context, "missing", state)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => entity.RunAsync(new TestEntityOperation(context, "Emit", state)).AsTask());
    }

    [Fact]
    public void Entity_context_preserves_options_and_records_handler_failures()
    {
        var context = new TestEntityContext(CounterId);
        var input = new object();
        var options = new StartOrchestrationOptions { InstanceId = "chosen" };
        Assert.Equal("chosen", context.ScheduleNewOrchestration("Child", input, options));
        Assert.Same(input, context.ScheduledOrchestrations[0].Input);
        Assert.Same(options, context.ScheduledOrchestrations[0].Options);
        var failure = new InvalidOperationException("failure");
        context.SignalHandler = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => context.SignalEntity(CounterId, "Add", 1)));
        Assert.Single(context.Signals);
        context.ScheduleHandler = _ => "";
        Assert.Throws<ArgumentException>(() => context.ScheduleNewOrchestration("Child"));
        Assert.Equal(2, context.ScheduledOrchestrations.Count);
    }

    [Fact]
    public async Task Invalid_entity_arguments_are_excluded_from_recordings()
    {
        var feature = new TestOrchestrationEntityFeature();
        var client = new TestDurableEntityClient();
        Assert.Throws<ArgumentNullException>(() => new TestEntityContext(default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => feature.CallEntityAsync<int>(default, "Get"));
        await Assert.ThrowsAsync<ArgumentException>(() => feature.SignalEntityAsync(CounterId, " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => feature.LockEntitiesAsync([default]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SignalEntityAsync(CounterId, " ", cancellation: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetEntityAsync(default, cancellation: TestContext.Current.CancellationToken));
        Assert.Empty(feature.Operations);
        Assert.Empty(client.Operations);
    }

    public sealed class CounterState
    {
        public int Value { get; set; }
    }

    public sealed class OutgoingEntity : TaskEntity<int>
    {
        public async Task<string> Emit(int amount, TaskEntityContext context)
        {
            await Task.Yield();
            State += amount;
            context.SignalEntity(context.Id, "Add", amount);
            return context.ScheduleNewOrchestration("FollowUp", amount);
        }
    }
}
