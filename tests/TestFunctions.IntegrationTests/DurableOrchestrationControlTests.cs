using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableOrchestrationControlTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Input_and_identity_are_configurable_without_serialization()
    {
        var context = CreateContext();
        Assert.Equal("test-orchestration", context.InstanceId);
        Assert.Null(context.GetInput<string>());
        Assert.Equal(0, context.GetInput<int>());

        var input = new WorkRequest(42);
        Assert.Same(context, context.WithInput(input).WithInstanceId("orders-42"));
        Assert.Same(input, context.GetInput<WorkRequest>());
        Assert.Equal("orders-42", context.InstanceId);
        Assert.Throws<InvalidOperationException>(() => context.GetInput<int>());
        context.WithInput(null);
        Assert.Null(context.GetInput<WorkRequest>());
    }

    [Fact]
    public void Existing_loggers_observe_test_controlled_replay_state()
    {
        using var factory = new RecordingLoggerFactory();
        var context = CreateContext().WithLoggerFactory(factory);
        var logger = context.CreateReplaySafeLogger<DurableOrchestrationControlTests>();
        var factoryLogger = context.ReplaySafeLoggerFactory.CreateLogger("Replay test");
        logger.LogInformation("Initial execution");
        context.WithReplayState(true);
        Assert.True(context.IsReplaying);
        logger.LogWarning("Replay suppressed");
        factoryLogger.LogWarning("Factory replay suppressed");
        context.WithReplayState(false);
        factoryLogger.LogInformation("Execution resumed");

        Assert.Equal(["Initial execution", "Execution resumed"], factory.Messages);
        Assert.False(factory.Disposed);
    }

    [Fact]
    public async Task Timers_use_manual_time_and_support_absolute_relative_and_past_deadlines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateContext();
        Assert.Equal(DateTime.UnixEpoch, context.CurrentUtcDateTime);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        context.AdvanceTimeTo(start);
        var first = context.CreateTimer(TimeSpan.FromMinutes(1), cancellationToken);
        var second = context.CreateTimer(start.AddMinutes(2), cancellationToken);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        Assert.Same(context, context.AdvanceTimeBy(TimeSpan.FromMinutes(1)));
        await first.WaitAsync(TestTimeout, cancellationToken);
        Assert.False(second.IsCompleted);
        Assert.Equal(start.AddMinutes(1), context.CurrentUtcDateTime);
        context.AdvanceTimeTo(start.AddMinutes(2));
        await second.WaitAsync(TestTimeout, cancellationToken);
        Assert.True(context.CreateTimer(start, cancellationToken).IsCompletedSuccessfully);
        Assert.True(context.CreateTimer(context.CurrentUtcDateTime, cancellationToken).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Timer_cancellation_preserves_the_token_and_does_not_change_time()
    {
        var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        var timer = context.CreateTimer(TimeSpan.FromDays(1), cancellation.Token);
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => timer);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(DateTime.UnixEpoch, context.CurrentUtcDateTime);
        var canceled = context.CreateTimer(DateTime.UnixEpoch, cancellation.Token);
        Assert.True(canceled.IsCanceled);
        context.AdvanceTimeBy(TimeSpan.FromDays(2));
        Assert.True(timer.IsCanceled);
    }

    [Fact]
    public async Task Manual_time_rejects_non_utc_backwards_and_negative_values()
    {
        var context = CreateContext();
        Assert.Throws<ArgumentException>(() => context.AdvanceTimeTo(DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified)));
        await Assert.ThrowsAsync<ArgumentException>(() => context.CreateTimer(
            DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Local), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.AdvanceTimeTo(DateTime.UnixEpoch.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.AdvanceTimeBy(TimeSpan.FromTicks(-1)));
        context.AdvanceTimeTo(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.AdvanceTimeBy(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public async Task Events_are_buffered_and_delivered_case_insensitively_in_fifo_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateContext();
        context.RaiseEvent("Approved", 1);
        context.RaiseEvent("APPROVED", 2);
        Assert.Equal(1, await context.WaitForExternalEvent<int>("approved", cancellationToken));
        Assert.Equal(2, await context.WaitForExternalEvent<int>("approved", cancellationToken));

        var first = context.WaitForExternalEvent<int>("approved", cancellationToken);
        var second = context.WaitForExternalEvent<int>("approved", cancellationToken);
        var unrelated = context.WaitForExternalEvent<int>("other", cancellationToken);
        context.RaiseEvent("Approved", 3);
        Assert.Equal(3, await first.WaitAsync(TestTimeout, cancellationToken));
        Assert.False(second.IsCompleted);
        Assert.False(unrelated.IsCompleted);
        context.RaiseEvent("APPROVED", 4);
        context.RaiseEvent("other", 5);
        Assert.Equal(4, await second.WaitAsync(TestTimeout, cancellationToken));
        Assert.Equal(5, await unrelated.WaitAsync(TestTimeout, cancellationToken));
    }

    [Fact]
    public async Task Canceled_event_waits_do_not_consume_buffered_or_future_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        var canceled = context.WaitForExternalEvent<string>("ready", cancellation.Token);
        var remaining = context.WaitForExternalEvent<string>("ready", cancellationToken);
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        context.RaiseEvent("ready", "first");
        Assert.Equal("first", await remaining.WaitAsync(TestTimeout, cancellationToken));

        context.RaiseEvent("ready", "buffered");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.WaitForExternalEvent<string>("ready", cancellation.Token));
        Assert.Equal("buffered", await context.WaitForExternalEvent<string>("ready", cancellationToken));
    }

    [Fact]
    public async Task Event_payloads_support_nullable_results_and_reject_wrong_types()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateContext();
        context.RaiseEvent("nullable", null);
        Assert.Null(await context.WaitForExternalEvent<string?>("nullable", cancellationToken));
        context.RaiseEvent("value", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.WaitForExternalEvent<int>("value", cancellationToken));
        var wrongType = context.WaitForExternalEvent<int>("wrong", cancellationToken);
        context.RaiseEvent("wrong", "text");
        await Assert.ThrowsAsync<InvalidOperationException>(() => wrongType);
        context.RaiseEvent("wrong", 42);
        Assert.Equal(42, await context.WaitForExternalEvent<int>("wrong", cancellationToken));
    }

    [Fact]
    public async Task Event_timeout_uses_manual_time_and_removes_the_expired_wait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateContext();
        var pending = context.WaitForExternalEvent<string>("ready", TimeSpan.FromMinutes(1), cancellationToken);
        Assert.False(pending.IsCompleted);
        context.AdvanceTimeBy(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TestTimeout, cancellationToken));
        Assert.True(pending.IsCanceled);
        context.RaiseEvent("ready", "after-timeout");
        Assert.Equal("after-timeout", await context.WaitForExternalEvent<string>("ready", cancellationToken).WaitAsync(TestTimeout, cancellationToken));
    }

    [Fact]
    public async Task Outbound_events_are_recorded_and_self_directed_events_are_delivered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateContext().WithInstanceId("parent");
        var ownEvent = context.WaitForExternalEvent<WorkRequest>("ready", cancellationToken);
        var payload = new WorkRequest(42);
        context.SendEvent("child", "ready", payload);
        Assert.False(ownEvent.IsCompleted);
        context.SendEvent(context.InstanceId, "ready", payload);
        Assert.Same(payload, await ownEvent.WaitAsync(TestTimeout, cancellationToken));
        Assert.Equal(["child", "parent"], context.SentEvents.Select(item => item.InstanceId));
        Assert.All(context.SentEvents, item => Assert.Same(payload, item.Payload));
    }

    [Fact]
    public async Task Sub_orchestrators_dispatch_to_real_children_and_preserve_options()
    {
        var context = CreateContext();
        var input = new WorkRequest(42);
        var options = new TaskOptions().WithInstanceId("child-42");
        TestOrchestrationContext? child = null;
        context.SubOrchestratorHandler = async call =>
        {
            Assert.Equal("Child", call.OrchestratorName);
            Assert.Same(input, call.Input);
            Assert.Same(options, call.Options);
            Assert.Equal(typeof(int), call.ResultType);
            child = new TestOrchestrationContext(activity => Task.FromResult<object?>(activity.Input))
                .WithInstanceId(Assert.IsType<SubOrchestrationOptions>(call.Options).InstanceId!)
                .WithInput(call.Input);
            return await child.CallActivityAsync<int>("Work", child.GetInput<WorkRequest>().Id);
        };

        Assert.Equal(42, await context.CallSubOrchestratorAsync<int>("Child", input, options));
        Assert.Equal("child-42", child!.InstanceId);
        Assert.Single(child.ActivityCalls);
        Assert.Single(context.SubOrchestrationCalls);
        Assert.Empty(context.ActivityCalls);
    }

    [Fact]
    public async Task Sub_orchestrator_failures_and_result_validation_are_explicit()
    {
        var context = CreateContext();
        Assert.Equal(0, await context.CallSubOrchestratorAsync<int>("Missing"));
        Assert.Single(context.SubOrchestrationCalls);
        Assert.Throws<ArgumentNullException>(() => context.SubOrchestratorHandler = null!);
        context.SubOrchestratorHandler = _ => Task.FromResult<object?>(null);
        Assert.Null(await context.CallSubOrchestratorAsync<string?>("Nullable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.CallSubOrchestratorAsync<int>("NullValue"));
        context.SubOrchestratorHandler = _ => Task.FromResult<object?>("wrong");
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.CallSubOrchestratorAsync<int>("Wrong"));

        var failure = new InvalidOperationException("Child failed.");
        context.SubOrchestratorHandler = _ => Task.FromException<object?>(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => context.CallSubOrchestratorAsync<int>("Failed")));
        Assert.Equal(["Missing", "Nullable", "NullValue", "Wrong", "Failed"], context.SubOrchestrationCalls.Select(call => call.OrchestratorName));
    }

    #region docs-functions-durable-controls

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

    #endregion

    [Fact]
    public async Task Concurrent_event_delivery_and_cancellation_do_not_lose_payloads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var context = CreateContext();
            using var cancellation = new CancellationTokenSource();
            var pending = context.WaitForExternalEvent<int>("race", cancellation.Token);
            await Task.WhenAll(
                Task.Run(() => cancellation.Cancel(), cancellationToken),
                Task.Run(() => context.RaiseEvent("race", iteration), cancellationToken));

            try
            {
                Assert.Equal(iteration, await pending.WaitAsync(TestTimeout, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Assert.Equal(iteration, await context.WaitForExternalEvent<int>("race", cancellationToken).WaitAsync(TestTimeout, cancellationToken));
            }
        }
    }

    [Fact]
    public async Task Configuration_and_event_arguments_are_validated()
    {
        var context = CreateContext();
        Assert.Throws<ArgumentNullException>(() => context.WithInstanceId(null!));
        Assert.Throws<ArgumentException>(() => context.WithInstanceId(" "));
        Assert.Throws<ArgumentNullException>(() => context.WithLoggerFactory(null!));
        Assert.Throws<ArgumentException>(() => context.RaiseEvent(" ", null));
        await Assert.ThrowsAsync<ArgumentException>(() => context.WaitForExternalEvent<int>(" ", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => context.SendEvent(" ", "ready", 1));
        Assert.Throws<ArgumentException>(() => context.SendEvent("other", " ", 1));
        Assert.Throws<ArgumentNullException>(() => context.SendEvent("other", "ready", null!));
    }

    [Fact]
    public async Task Parallel_dispatch_preserves_all_recorded_calls_and_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = new TestOrchestrationContext(call => Task.FromResult(call.Input));
        context.SubOrchestratorHandler = call => Task.FromResult(call.Input);
        var runs = Enumerable.Range(0, 100).Select(index => Task.Run(async () =>
        {
            context.SendEvent("other", "work", index);
            Assert.Equal(index, await context.CallActivityAsync<int>("Work", index));
            Assert.Equal(index, await context.CallSubOrchestratorAsync<int>("Child", index));
        }, cancellationToken));

        await Task.WhenAll(runs).WaitAsync(TestTimeout, cancellationToken);
        Assert.Equal(100, context.ActivityCalls.Count);
        Assert.Equal(100, context.SubOrchestrationCalls.Count);
        Assert.Equal(100, context.SentEvents.Count);
        Assert.Equal(100, context.ActivityCalls.Select(call => call.Input).Distinct().Count());
    }

    private static TestOrchestrationContext CreateContext() => new(_ => Task.FromResult<object?>(null));

    #region docs-functions-durable-workflow

    private static async Task<int> RunWorkflowAsync(TaskOrchestrationContext context, CancellationToken cancellationToken)
    {
        var input = context.GetInput<WorkRequest>();
        await context.CreateTimer(TimeSpan.FromMinutes(5), cancellationToken);
        var approved = await context.WaitForExternalEvent<bool>("Approved", cancellationToken);
        return approved ? await context.CallSubOrchestratorAsync<int>("Child", input) : 0;
    }

    private sealed record WorkRequest(int Id);

    #endregion

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<string> Messages { get; } = [];
        public bool Disposed { get; private set; }
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingLogger(List<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state)!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }
}
