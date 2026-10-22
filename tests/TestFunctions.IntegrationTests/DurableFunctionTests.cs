using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableFunctionTests
{
    #region docs-functions-durable-activity

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

    #endregion

    [Fact]
    public void Replay_safe_logging_is_supported_during_in_process_execution()
    {
        var context = new TestOrchestrationContext(_ => Task.FromResult<object?>(null));
        ILogger[] loggers =
        [
            context.CreateReplaySafeLogger<DurableFunctionTests>(),
            context.CreateReplaySafeLogger(typeof(DurableFunctionTests)),
            context.CreateReplaySafeLogger("Orchestrator"),
            context.ReplaySafeLoggerFactory.CreateLogger("Orchestrator")
        ];

        Assert.False(context.IsReplaying);
        foreach (var logger in loggers)
        {
            Assert.False(logger.IsEnabled(LogLevel.Information));
            using var scope = logger.BeginScope("Activity dispatch");
            logger.LogInformation("Dispatching activity {ActivityName}", nameof(GreetingActivity));
        }

        Assert.Empty(context.ActivityCalls);
    }

    #region docs-functions-durable-limitations

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

    #endregion

    [Fact]
    public async Task Activity_dispatch_validates_handlers_nullable_results_and_result_types()
    {
        Assert.Throws<ArgumentNullException>(() => new TestOrchestrationContext(null!));
        var context = new TestOrchestrationContext(_ => Task.FromResult<object?>(null));
        Assert.Throws<ArgumentNullException>(() => context.ActivityHandler = null!);
        Assert.Null(await context.CallActivityAsync<string?>(new TaskName("nullable")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.CallActivityAsync<int>(new TaskName("null-value-type")));

        context.ActivityHandler = _ => Task.FromResult<object?>("wrong");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.CallActivityAsync<int>(new TaskName("wrong-type")));
    }

    private static class GreetingOrchestrator
    {
        public static async Task<string> RunAsync(TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger(nameof(GreetingOrchestrator));
            logger.LogInformation("Scheduling activity {ActivityName}", nameof(GreetingActivity));
            var result = await context.CallActivityAsync<string>(
                new TaskName(nameof(GreetingActivity)),
                "Ada");
            logger.LogInformation("Activity {ActivityName} completed", nameof(GreetingActivity));
            return result;
        }
    }

    private sealed class GreetingActivity(string prefix)
    {
        public Task<string> RunAsync(string name) =>
            Task.FromResult($"{prefix}, {name}!");
    }
}
