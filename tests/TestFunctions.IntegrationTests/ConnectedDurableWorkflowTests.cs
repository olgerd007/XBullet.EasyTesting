using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class ConnectedDurableWorkflowTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    #region docs-functions-connected-workflow

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

    #endregion

    [Fact]
    public async Task Void_activities_and_orchestrators_complete_with_null_results()
    {
        var saved = new ConcurrentQueue<int>();
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddActivity<WorkflowFunction, int>("Save", (_, input, _) =>
            {
                saved.Enqueue(input);
                return Task.CompletedTask;
            })
            .AddOrchestrator<WorkflowFunction>("Order", (_, context, _) => context.CallActivityAsync("Save", 42));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Null(await workflow.GetResultAsync<object?>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(42, Assert.Single(saved));
    }

    [Fact]
    public async Task Named_children_use_isolated_contexts_parent_identity_and_manual_time()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        var timerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Parent", async (_, context, _) =>
            {
                context.AdvanceTimeBy(TimeSpan.FromHours(1));
                return await context.CallSubOrchestratorAsync<int>("Child", context.GetInput<int>(),
                    new SubOrchestrationOptions { InstanceId = "child-fixed", Version = "v2" });
            })
            .AddOrchestrator<WorkflowFunction, int>("Child", async (_, context, worker) =>
            {
                var value = await context.CallActivityAsync<int>("Add", context.GetInput<int>());
                var timer = context.CreateTimer(context.CurrentUtcDateTime.AddMinutes(5), worker.CancellationToken);
                timerReady.SetResult();
                await timer;
                return value;
            })
            .AddActivity<WorkflowFunction, int, int>("Add", (_, input, _) => Task.FromResult(input + 2));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Parent", 40,
            new StartOrchestrationOptions { InstanceId = "parent", Version = "v1" }, token);
        await timerReady.Task.WaitAsync(Timeout, token);
        var child = workflow.GetContext("child-fixed");
        Assert.Equal("parent", child.Parent!.InstanceId);
        Assert.Equal("v2", child.Version);
        Assert.Equal(DateTime.UnixEpoch.AddHours(1), child.CurrentUtcDateTime);
        Assert.Equal("v1", workflow.GetContext(id).Version);
        child.AdvanceTimeBy(TimeSpan.FromMinutes(5));

        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Single(workflow.GetContext(id).SubOrchestrationCalls);
        Assert.Equal(42, await workflow.GetResultAsync<int>("child-fixed", token));
        var metadata = await workflow.Client.GetInstanceAsync("child-fixed", token);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(1), metadata!.CreatedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(65), metadata.LastUpdatedAt);
    }

    [Fact]
    public async Task Events_route_by_instance_from_client_and_other_orchestrators_without_duplicate_self_delivery()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Receiver", (_, context, _) => context.WaitForExternalEvent<int>("Done"))
            .AddOrchestrator<WorkflowFunction>("Sender", (_, context, _) =>
            {
                context.SendEvent(context.GetInput<string>(), "Done", 7);
                return Task.CompletedTask;
            })
            .AddOrchestrator<WorkflowFunction, int>("Self", (_, context, _) =>
            {
                context.SendEvent(context.InstanceId, "Done", 9);
                return context.WaitForExternalEvent<int>("Done");
            });
        var token = TestContext.Current.CancellationToken;
        var first = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Receiver", null, new() { InstanceId = "first" }, token);
        var second = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Receiver", null, new() { InstanceId = "second" }, token);
        await workflow.Client.RaiseEventAsync(second, "DONE", 42, token);
        var sender = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Sender", first, token);
        Assert.Null(await workflow.GetResultAsync<object?>(sender, token).WaitAsync(Timeout, token));
        Assert.Equal(7, await workflow.GetResultAsync<int>(first, token).WaitAsync(Timeout, token));
        Assert.Equal(42, await workflow.GetResultAsync<int>(second, token).WaitAsync(Timeout, token));
        var self = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Self", token);
        Assert.Equal(9, await workflow.GetResultAsync<int>(self, token).WaitAsync(Timeout, token));
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var another = workflow.GetContext(self).WaitForExternalEvent<int>("Done", waitCancellation.Token);
        Assert.False(another.IsCompleted);
        await waitCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => another);
        await workflow.Client.RaiseEventAsync(first, "Done", 100, token);
        Assert.Equal(7, await workflow.GetResultAsync<int>(first, token));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => workflow.Client.RaiseEventAsync("FIRST", "Done", token));
    }

    [Fact]
    public async Task Activity_failures_propagate_original_exception_and_produce_failed_metadata()
    {
        var failure = new InvalidOperationException("activity unavailable");
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<WorkflowFunction, string>("Order", (_, context, _) => context.CallActivityAsync<string>("Fail", 42))
            .AddActivity<WorkflowFunction, int, string>("Fail", (_, _, _) => Task.FromException<string>(failure));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<string>(id, token).WaitAsync(Timeout, token)));
        var metadata = await workflow.Client.WaitForInstanceCompletionAsync(id, true, token);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal("activity unavailable", metadata.FailureDetails!.ErrorMessage);
        Assert.Single(workflow.GetContext(id).ActivityCalls);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("WrongInput")]
    [InlineData("WrongResult")]
    [InlineData("Continuation")]
    public async Task Dispatch_errors_fail_the_instance_explicitly(string error)
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddActivity<WorkflowFunction, int, int>("Number", (_, input, _) => Task.FromResult(input))
            .AddOrchestrator<WorkflowFunction, object?>("Order", async (_, context, _) =>
            {
                switch (error)
                {
                    case "Missing":
                        return await context.CallActivityAsync<int>("missing");
                    case "WrongInput":
                        return await context.CallActivityAsync<int>("Number", "text");
                    case "WrongResult":
                        return await context.CallActivityAsync<string>("Number", 1);
                    default:
                        context.ContinueAsNew();
                        return null;
                }
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<object?>(id, token).WaitAsync(Timeout, token));
        Assert.Equal(OrchestrationRuntimeStatus.Failed, (await workflow.Client.GetInstanceAsync(id, token))!.RuntimeStatus);
    }

    [Fact]
    public async Task Middleware_short_circuit_does_not_report_successful_execution()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>()
            .UseMiddleware((_, _) => Task.CompletedTask).Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, _, _) => Task.FromResult(42));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Contains("short-circuited", error.Message);
    }

    [Fact]
    public async Task Registration_duplicates_missing_routes_and_reused_ids_fail_without_replacing_execution()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, _, _) => Task.FromResult(42));
        var token = TestContext.Current.CancellationToken;
        Assert.Throws<ArgumentException>(() => workflow.AddOrchestrator<WorkflowFunction, int>("Order", (_, _, _) => Task.FromResult(0)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.Client.ScheduleNewOrchestrationInstanceAsync("order", token));
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", null, new() { InstanceId = "id" }, token);
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Throws<InvalidOperationException>(() => workflow.AddActivity<WorkflowFunction, int, int>("Late", (_, input, _) => Task.FromResult(input)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", null, new() { InstanceId = "id" }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", null,
            new() { StartAt = DateTimeOffset.UnixEpoch.AddDays(1) }, token));
        Assert.Null(await workflow.Client.GetInstanceAsync("missing", token));
        Assert.Throws<KeyNotFoundException>(() => workflow.GetContext("missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<string>(id, token));
    }

    public sealed class WorkflowFunction;

    public sealed class ExecutionSignals
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<ScopeObservation> MiddlewareScopes { get; } = new();
        public ConcurrentQueue<ScopeObservation> FunctionScopes { get; } = new();
    }

    public sealed record ScopeObservation(string InvocationId, ScopeMarker Scope);

    public sealed class ScopeMarker : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class ScopeMiddleware(ExecutionSignals signals, ScopeMarker scope) : IFunctionsWorkerMiddleware
    {
        public Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            Assert.Same(scope, context.InstanceServices.GetRequiredService<ScopeMarker>());
            signals.MiddlewareScopes.Enqueue(new(context.InvocationId, scope));
            return next(context);
        }
    }

    public sealed class OrderOrchestrator(ExecutionSignals signals, ScopeMarker scope)
    {
        public async Task<string> RunAsync(TaskOrchestrationContext context, FunctionContext worker)
        {
            signals.FunctionScopes.Enqueue(new(worker.InvocationId, scope));
            var input = context.GetInput<string>();
            var first = await context.CallActivityAsync<string>("Price", input);
            var second = await context.CallActivityAsync<string>("Price", first);
            context.SetCustomStatus("awaiting approval");
            signals.Ready.TrySetResult();
            return await context.WaitForExternalEvent<bool>("Approved", worker.CancellationToken) ? second : "rejected";
        }
    }

    public sealed class PriceActivity(ExecutionSignals signals, ScopeMarker scope)
    {
        public Task<string> RunAsync(string input, FunctionContext worker)
        {
            worker.CancellationToken.ThrowIfCancellationRequested();
            signals.FunctionScopes.Enqueue(new(worker.InvocationId, scope));
            return Task.FromResult($"priced: {input}");
        }
    }
}
