using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class ConnectedDurableWorkflowLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    #region docs-functions-connected-scenario

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

    #endregion

    [Fact]
    public async Task Existing_client_can_be_injected_into_starters_and_scheduling_cancellation_does_not_stop_execution()
    {
        var client = new TestDurableTaskClient();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<InjectedStarter>()
            .AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>()
            .ConfigureServices(services => services.AddSingleton<DurableTaskClient>(client)).Build();
        await using var workflow = new TestDurableWorkflow(client).UseHost(host)
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction, int>("Approval", (_, context, _) => context.WaitForExternalEvent<int>("Done"));
        using var scheduling = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;
        var result = await host.InvokeAsync<InjectedStarter, string>(host.CreateContext("Starter", scheduling.Token),
            (starter, worker) => starter.RunAsync(worker.CancellationToken));
        var id = result.Result!;
        await workflow.Client.WaitForInstanceStartAsync(id, cancellation: token).WaitAsync(Timeout, token);
        await scheduling.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.GetResultAsync<int>(id, scheduling.Token));
        Assert.Equal(OrchestrationRuntimeStatus.Running, (await workflow.Client.GetInstanceAsync(id, token))!.RuntimeStatus);
        await workflow.Client.RaiseEventAsync(id, "Done", 42, token);
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_of_connected_waits_preserves_the_caller_token(bool timer)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction>("Wait", async (_, context, _) =>
            {
                var waiting = timer ? context.CreateTimer(DateTime.UnixEpoch.AddDays(1), caller.Token)
                    : context.WaitForExternalEvent<string>("Done", caller.Token);
                entered.SetResult();
                await waiting;
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Wait", token);
        await entered.Task.WaitAsync(Timeout, token);
        await caller.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.GetResultAsync<object?>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(caller.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_cancels_tokenless_event_or_timer_waits_and_keeps_registrations(bool timer)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction>("Wait", async (_, context, _) =>
            {
                try
                {
                    var pending = timer ? context.CreateTimer(DateTime.UnixEpoch.AddDays(1), default)
                        : context.WaitForExternalEvent<string>("Done");
                    entered.TrySetResult();
                    await pending;
                }
                finally
                {
                    finished.TrySetResult();
                }
            });
        var token = TestContext.Current.CancellationToken;
        var first = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Wait", token);
        await entered.Task.WaitAsync(Timeout, token);
        await workflow.ResetAsync(token);
        Assert.True(finished.Task.IsCompleted);
        workflow.Client.Should().BeEmpty();
        var second = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Wait", token);
        Assert.Equal(first, second);
        await workflow.Client.WaitForInstanceStartAsync(second, cancellation: token).WaitAsync(Timeout, token);
    }

    [Fact]
    public async Task Cleanup_timeout_preserves_live_scopes_until_user_code_finishes_and_allows_retry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConnectedDurableWorkflowTests.ScopeMarker? marker = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WaitingFunction>()
            .ConfigureServices(services => services.AddScoped<ConnectedDurableWorkflowTests.ScopeMarker>()).Build();
        await using var workflow = new TestDurableWorkflow(shutdownTimeout: TimeSpan.FromMilliseconds(50)).UseHost(host)
            .AddOrchestrator<WaitingFunction>("Blocked", async (function, _, _) =>
            {
                marker = function.Scope;
                entered.SetResult();
                await release.Task;
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Blocked", token);
        await entered.Task.WaitAsync(Timeout, token);
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(async () => await workflow.ResetAsync(token));
            Assert.False(marker!.Disposed);
            Assert.Contains("Blocked", JsonSerializer.Serialize(await workflow.CaptureDiagnosticsAsync(token)));
        }
        finally
        {
            release.TrySetResult();
        }

        var metadata = await workflow.Client.WaitForInstanceCompletionAsync(id, cancellation: token).WaitAsync(Timeout, token);
        Assert.Equal(OrchestrationRuntimeStatus.Terminated, metadata.RuntimeStatus);
        Assert.True(marker!.Disposed);
        await workflow.ResetAsync(token);
        workflow.Client.Should().BeEmpty();
    }

    [Fact]
    public async Task Pending_completion_wait_returns_terminal_metadata_when_reset_clears_the_registry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction, string>("Wait", (_, context, _) =>
            {
                var pending = context.WaitForExternalEvent<string>("Done");
                entered.SetResult();
                return pending;
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Wait", token);
        await entered.Task.WaitAsync(Timeout, token);
        var completed = workflow.Client.WaitForInstanceCompletionAsync(id, cancellation: token);
        await workflow.ResetAsync(token);
        Assert.Equal(OrchestrationRuntimeStatus.Terminated, (await completed.WaitAsync(Timeout, token)).RuntimeStatus);
        workflow.Client.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_drains_activities_still_running_after_the_orchestrator_returns()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string>? background = null;
        ConnectedDurableWorkflowTests.ScopeMarker? marker = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WaitingFunction>()
            .AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>()
            .ConfigureServices(services => services.AddScoped<ConnectedDurableWorkflowTests.ScopeMarker>()).Build();
        await using var workflow = new TestDurableWorkflow(shutdownTimeout: TimeSpan.FromMilliseconds(50)).UseHost(host)
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction, int>("Order", (_, context, _) =>
            {
                background = context.CallActivityAsync<string>("Slow", 1);
                return Task.FromResult(42);
            })
            .AddActivity<WaitingFunction, int, string>("Slow", async (function, _, _) =>
            {
                marker = function.Scope;
                entered.SetResult();
                await release.Task;
                return "finished";
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await entered.Task.WaitAsync(Timeout, token);
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(async () => await workflow.ResetAsync(token));
            Assert.False(marker!.Disposed);
            Assert.Equal(42, await workflow.GetResultAsync<int>(id, token));
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.Equal("finished", await background!.WaitAsync(Timeout, token));
        Assert.True(marker!.Disposed);
        await workflow.ResetAsync(token);
        workflow.Client.Should().BeEmpty();
    }

    [Fact]
    public async Task Canceled_reset_leaves_execution_intact_and_disposal_rejects_further_use()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>().Build();
        var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction, int>("Order", (_, _, _) => Task.FromResult(42));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await workflow.ResetAsync(canceled.Token));
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token));
        await workflow.DisposeAsync();
        await workflow.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token));
        Assert.Throws<ObjectDisposedException>(() => workflow.GetContext(id));
        Assert.NotNull(host.CreateContext("StillOwnedByCaller", token));
    }

    [Fact]
    public async Task Host_attachment_arguments_and_preconfigured_clients_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestDurableWorkflow(shutdownTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestDurableWorkflow(shutdownTimeout: System.Threading.Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentException>(() => new TestDurableWorkflow(new() { ScheduleHandler = (_, _) => Task.FromResult("custom") }));
        await using var workflow = new TestDurableWorkflow()
            .AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction, int>("Order", (_, _, _) => Task.FromResult(42));
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token));
        Assert.Throws<ArgumentNullException>(() => workflow.UseHost(null!));
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<ConnectedDurableWorkflowTests.WorkflowFunction>().Build();
        workflow.UseHost(host);
        Assert.Throws<InvalidOperationException>(() => workflow.UseHost(host));
        Assert.Throws<ArgumentException>(() => workflow.AddActivity<ConnectedDurableWorkflowTests.WorkflowFunction, int>(" ", (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => workflow.AddOrchestrator<ConnectedDurableWorkflowTests.WorkflowFunction>("Other", null!));
    }

    public sealed class WaitingFunction(ConnectedDurableWorkflowTests.ScopeMarker scope)
    {
        public ConnectedDurableWorkflowTests.ScopeMarker Scope { get; } = scope;
    }

    public sealed class InjectedStarter(DurableTaskClient client)
    {
        public Task<string> RunAsync(CancellationToken cancellationToken) => client.ScheduleNewOrchestrationInstanceAsync("Approval", cancellationToken);
    }
}
