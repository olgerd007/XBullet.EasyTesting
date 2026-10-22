using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableOrchestrationStubTests
{
    [Fact]
    public async Task Parameterless_context_returns_typed_default_dispatch_results()
    {
        var context = new TestOrchestrationContext();
        Assert.Equal(0, await context.CallActivityAsync<int>("Count"));
        Assert.Null(await context.CallActivityAsync<string>("Text"));
        Assert.Null(await context.CallActivityAsync<int?>("Nullable"));
        Assert.Equal(0, (await context.CallActivityAsync<ConstructedValue>("Struct")).Value);
        await context.CallActivityAsync("NoResult");
        Assert.Equal(0, await context.CallSubOrchestratorAsync<int>("Count"));
        Assert.Null(await context.CallSubOrchestratorAsync<string>("Text"));
        await context.CallSubOrchestratorAsync("NoResult");
        Assert.Equal(5, context.ActivityCalls.Count);
        Assert.Equal(3, context.SubOrchestrationCalls.Count);
    }

    #region docs-functions-durable-overrides

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

    #endregion

    [Fact]
    public void Property_overrides_are_copied_and_parent_can_be_cleared()
    {
        var properties = new Dictionary<string, object?> { ["tenant"] = "one" };
        var context = new TestOrchestrationContext().WithProperties(properties)
            .WithParent(new ParentOrchestrationInstance("Parent", "parent-1"));
        properties["tenant"] = "two";
        properties["new"] = true;
        Assert.Equal("one", context.Properties["tenant"]);
        Assert.DoesNotContain("new", context.Properties.Keys);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)context.Properties).Add("other", 42));
        Assert.Same(context, context.WithParent(null));
        Assert.Null(context.Parent);
    }

    [Fact]
    public void Default_guid_sequences_are_repeatable_and_instance_specific()
    {
        var first = new TestOrchestrationContext().WithInstanceId("one");
        var second = new TestOrchestrationContext().WithInstanceId("one");
        var other = new TestOrchestrationContext().WithInstanceId("two");
        var sequence = Enumerable.Range(0, 10).Select(_ => first.NewGuid()).ToArray();
        Assert.Equal(sequence, Enumerable.Range(0, 10).Select(_ => second.NewGuid()));
        Assert.Equal(10, sequence.Distinct().Count());
        Assert.DoesNotContain(other.NewGuid(), sequence);
        var generated = 0;
        first.WithGuidGenerator(() => { generated++; return Guid.Empty; });
        Assert.Equal(Guid.Empty, first.NewGuid());
        Assert.Equal(1, generated);
    }

    [Fact]
    public void Version_ordering_is_ordinal_by_default_and_can_be_overridden()
    {
        var context = new TestOrchestrationContext();
        Assert.Equal(0, context.CompareVersionTo(string.Empty));
        Assert.True(context.CompareVersionTo("1") < 0);
        context.WithVersion("2.0");
        Assert.True(context.CompareVersionTo(string.Empty) > 0);
        Assert.Equal(0, context.CompareVersionTo("2.0"));
        Assert.True(context.CompareVersionTo("10.0") > 0);
        context.WithVersionComparer((current, other) => Version.Parse(current).CompareTo(Version.Parse(other)));
        Assert.True(context.CompareVersionTo("10.0") < 0);
    }

    [Fact]
    public async Task Entity_defaults_support_calls_signals_and_local_lock_markers()
    {
        var context = new TestOrchestrationContext();
        var first = new EntityInstanceId("Counter", "one");
        var second = new EntityInstanceId("Counter", "two");
        Assert.Equal(0, await context.Entities.CallEntityAsync<int>(first, "Get"));
        Assert.Null(await context.Entities.CallEntityAsync<string>(first, "Get"));
        await context.Entities.CallEntityAsync(first, "Add", 1);
        await context.Entities.SignalEntityAsync(first, "Add", 1);
        Assert.False(context.Entities.InCriticalSection());

        var outer = await context.Entities.LockEntitiesAsync(first, first);
        var inner = await context.Entities.LockEntitiesAsync(second);
        Assert.True(context.Entities.InCriticalSection(out var locked));
        Assert.Equal([first, second], locked);
        await outer.DisposeAsync();
        Assert.True(context.Entities.InCriticalSection(out locked));
        Assert.Equal([second], locked);
        await inner.DisposeAsync();
        await inner.DisposeAsync();
        Assert.False(context.Entities.InCriticalSection());
    }

    [Fact]
    public async Task Caller_supplied_entity_feature_replaces_the_default_stub()
    {
        var feature = new CustomEntityFeature();
        var context = new TestOrchestrationContext().WithEntities(feature);
        var entity = new EntityInstanceId("Counter", "one");
        Assert.Same(feature, context.Entities);
        Assert.Equal(42, await context.Entities.CallEntityAsync<int>(entity, "Get"));
        await context.Entities.SignalEntityAsync(entity, "Add", 3);
        Assert.Equal(3, feature.SignalInput);
    }

    [Fact]
    public void Status_updates_are_retained_before_callbacks_and_null_clears_status()
    {
        var updates = new List<object?>();
        var context = new TestOrchestrationContext().WithCustomStatusHandler(updates.Add).WithCustomStatus("ready");
        context.SetCustomStatus(new { Completed = 2 });
        Assert.Same(updates[1], context.CustomStatus);
        context.SetCustomStatus(null);
        Assert.Null(context.CustomStatus);
        Assert.Equal(3, updates.Count);
        var failure = new InvalidOperationException("Status callback failed.");
        context.WithCustomStatusHandler(_ => throw failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => context.SetCustomStatus("failed")));
        Assert.Equal("failed", context.CustomStatus);
    }

    [Fact]
    public async Task Continuation_records_options_without_resetting_input_version_or_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var callbacks = new List<ContinueAsNewOptions>();
        var context = new TestOrchestrationContext().WithInput("current").WithVersion("1")
            .WithContinueAsNewHandler(callbacks.Add);
        context.RaiseEvent("ready", "buffered");
        context.ContinueAsNew("next", preserveUnprocessedEvents: false);
        var options = new ContinueAsNewOptions { NewInput = 42, NewVersion = "2", PreserveUnprocessedEvents = true };
        context.ContinueAsNew(options);
        options.NewInput = "mutated";

        Assert.Equal(2, context.ContinueAsNewRequests.Count);
        Assert.False(context.ContinueAsNewRequests[0].PreserveUnprocessedEvents);
        Assert.Equal("next", context.ContinueAsNewRequests[0].NewInput);
        Assert.Equal(42, context.ContinueAsNewRequests[1].NewInput);
        Assert.Equal("2", context.ContinueAsNewRequests[1].NewVersion);
        Assert.Equal(context.ContinueAsNewRequests, callbacks);
        Assert.Equal("current", context.GetInput<string>());
        Assert.Equal("1", context.Version);
        Assert.Equal("buffered", await context.WaitForExternalEvent<string>("ready", cancellationToken));
        var failure = new InvalidOperationException("Continuation callback failed.");
        context.WithContinueAsNewHandler(_ => throw failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => context.ContinueAsNew("failure")));
        Assert.Equal("failure", context.ContinueAsNewRequests[2].NewInput);
    }

    [Fact]
    public void Fluent_override_arguments_are_validated()
    {
        var context = new TestOrchestrationContext();
        Assert.ThrowsAny<ArgumentException>(() => context.WithName(default));
        Assert.Throws<ArgumentNullException>(() => context.WithVersion(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithProperties(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithEntities(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithGuidGenerator(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithVersionComparer(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithActivityHandler(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithSubOrchestratorHandler(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithCustomStatusHandler(null!));
        Assert.Throws<ArgumentNullException>(() => context.WithContinueAsNewHandler(null!));
        Assert.Throws<ArgumentNullException>(() => context.ContinueAsNew(null!));
        Assert.Throws<ArgumentNullException>(() => context.CompareVersionTo(null!));
    }

    private readonly struct ConstructedValue
    {
        public ConstructedValue() => Value = 42;
        public int Value { get; }
    }

    private sealed class CustomEntityFeature : TaskOrchestrationEntityFeature
    {
        public object? SignalInput { get; private set; }
        public override Task<TResult> CallEntityAsync<TResult>(
            EntityInstanceId id,
            string operationName,
            object? input = null,
            CallEntityOptions? options = null) =>
            Task.FromResult((TResult)(object)42);

        public override Task CallEntityAsync(
            EntityInstanceId id,
            string operationName,
            object? input = null,
            CallEntityOptions? options = null) => Task.CompletedTask;

        public override Task SignalEntityAsync(
            EntityInstanceId id,
            string operationName,
            object? input = null,
            SignalEntityOptions? options = null)
        {
            SignalInput = input;
            return Task.CompletedTask;
        }

        public override Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds) =>
            Task.FromResult<IAsyncDisposable>(new EmptyScope());

        public override bool InCriticalSection(out IReadOnlyList<EntityInstanceId> entityIds)
        {
            entityIds = [];
            return false;
        }
    }

    private sealed class EmptyScope : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
