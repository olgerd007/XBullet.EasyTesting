using Microsoft.DurableTask;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableFunctionTests
{
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

    [Fact]
    public void Runtime_dependent_members_are_not_supported()
    {
        var context = new TestOrchestrationContext(_ => Task.FromResult<object?>(null));

        Assert.Throws<NotSupportedException>(() => _ = context.InstanceId);
        Assert.Throws<NotSupportedException>(() => context.GetInput<string>());
        Assert.Throws<NotSupportedException>(() => context.NewGuid());
    }

    private static class GreetingOrchestrator
    {
        public static Task<string> RunAsync(TaskOrchestrationContext context) =>
            context.CallActivityAsync<string>(
                new TaskName(nameof(GreetingActivity)),
                "Ada");
    }

    private sealed class GreetingActivity(string prefix)
    {
        public Task<string> RunAsync(string name) =>
            Task.FromResult($"{prefix}, {name}!");
    }
}
