using Microsoft.DurableTask;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableNamedActivityHandlerTests
{
    #region docs-functions-durable-named-activities

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

    #endregion

    [Fact]
    public async Task Registrations_match_exact_names_replace_previous_handlers_and_override_fallback()
    {
        var context = new TestOrchestrationContext(_ => Task.FromResult<object?>(-1));
        Assert.Same(context, context.WithActivityHandler("Work", (_, _) => Task.FromResult<object?>(1)));
        context.WithActivityHandler("Work", (_, _) => Task.FromResult<object?>(2))
            .WithActivityHandler(_ => Task.FromResult<object?>(3));

        var options = new TaskOptions();
        var input = new { Id = 42 };
        Assert.Equal(2, await context.CallActivityAsync<int>("Work", input, options));
        var call = Assert.Single(context.ActivityCalls);
        Assert.Same(input, call.Input);
        Assert.Same(options, call.Options);
        Assert.Equal(typeof(int), call.ResultType);
        Assert.Equal(3, await context.CallActivityAsync<int>("work"));
        Assert.Equal(3, await context.CallActivityAsync<int>("Other"));
    }

    [Fact]
    public async Task Replacing_a_registration_does_not_change_an_in_flight_call()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TestOrchestrationContext().WithActivityHandler("Work", (_, _) => completion.Task);
        var pending = context.CallActivityAsync<int>("Work");
        context.WithActivityHandler("Work", (_, _) => Task.FromResult<object?>(2));
        Assert.Equal(2, await context.CallActivityAsync<int>("Work"));
        completion.SetResult(1);
        Assert.Equal(1, await pending.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
        Assert.Equal(2, context.ActivityCalls.Count);
    }

    [Fact]
    public async Task Named_handler_results_use_existing_type_validation_and_failures_propagate()
    {
        var context = new TestOrchestrationContext().WithActivityHandler("Work", (_, _) => Task.FromResult<object?>(null));
        Assert.Null(await context.CallActivityAsync<string?>("Work"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.CallActivityAsync<int>("Work"));
        context.WithActivityHandler("Work", (_, _) => Task.FromResult<object?>("wrong"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.CallActivityAsync<int>("Work"));

        var failure = new InvalidOperationException("Activity failed.");
        context.WithActivityHandler("Work", (_, _) => Task.FromException<object?>(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => context.CallActivityAsync<int>("Work")));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        context.WithActivityHandler("Work", (_, _) => Task.FromCanceled<object?>(cancellation.Token));
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.CallActivityAsync<int>("Work"));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(5, context.ActivityCalls.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Named_handler_rejects_missing_activity_names(string? activityName)
    {
        var context = new TestOrchestrationContext();
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            context.WithActivityHandler(activityName!, (_, _) => Task.FromResult<object?>(null)));
        Assert.Equal("activityName", exception.ParamName);
    }

    [Fact]
    public void Named_handler_rejects_null_callback()
    {
        var context = new TestOrchestrationContext();
        var exception = Assert.Throws<ArgumentNullException>(() => context.WithActivityHandler("Work", null!));
        Assert.Equal("handler", exception.ParamName);
    }
}
