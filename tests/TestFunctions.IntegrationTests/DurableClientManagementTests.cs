using System.Text.Json;
using DurableTask.Core.History;
using Microsoft.DurableTask.Client;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableClientManagementTests
{
    [Fact]
    public async Task Management_calls_preserve_options_tokens_results_and_attempt_order()
    {
        var token = TestContext.Current.CancellationToken;
        var termination = new TerminateInstanceOptions(new { Reason = "cancel" }, true);
        var purgeOptions = new PurgeInstanceOptions(true);
        var filter = new PurgeInstancesFilter(null, null, [OrchestrationRuntimeStatus.Completed]);
        var purged = new PurgeResult(2);
        IList<HistoryEvent> history = [new GenericEvent(1, "test history")];
        await using var client = new TestDurableTaskClient
        {
            TerminateHandler = (id, options, cancellation) =>
            {
                Assert.Equal("order-42", id);
                Assert.Same(termination, options);
                Assert.Equal(token, cancellation);
                return Task.CompletedTask;
            },
            PurgeHandler = (id, options, cancellation) =>
            {
                Assert.Equal("order-42", id);
                Assert.Same(purgeOptions, options);
                Assert.Equal(token, cancellation);
                return Task.FromResult(purged);
            },
            PurgeAllHandler = (query, options, cancellation) =>
            {
                Assert.Same(filter, query);
                Assert.Same(purgeOptions, options);
                Assert.Equal(token, cancellation);
                return Task.FromResult(purged);
            },
            RestartHandler = (id, newId, cancellation) =>
            {
                Assert.Equal("order-42", id);
                Assert.True(newId);
                Assert.Equal(token, cancellation);
                return Task.FromResult("order-43");
            },
            RewindHandler = (id, reason, cancellation) =>
            {
                Assert.Equal("order-42", id);
                Assert.Equal("dependency repaired", reason);
                Assert.Equal(token, cancellation);
                return Task.CompletedTask;
            },
            HistoryHandler = (id, cancellation) =>
            {
                Assert.Equal("order-42", id);
                Assert.Equal(token, cancellation);
                return Task.FromResult(history);
            }
        };
        DurableTaskClient sdk = client;

        await sdk.TerminateInstanceAsync("order-42", termination, token);
        Assert.Same(purged, await sdk.PurgeInstanceAsync("order-42", purgeOptions, token));
        Assert.Same(purged, await sdk.PurgeAllInstancesAsync(filter, purgeOptions, token));
        Assert.Equal("order-43", await sdk.RestartAsync("order-42", true, token));
        await sdk.RewindInstanceAsync("order-42", "dependency repaired", token);
        Assert.Same(history, await sdk.GetOrchestrationHistoryAsync("order-42", token));

        client.Should().HaveCount(6).HaveOperationSequence(
            DurableClientOperation.Terminate, DurableClientOperation.PurgeInstance, DurableClientOperation.PurgeAll,
            DurableClientOperation.Restart, DurableClientOperation.Rewind, DurableClientOperation.GetHistory);
        Assert.Equal(Enumerable.Range(1, 6).Select(index => (long)index), client.Operations.Select(call => call.Sequence));
        Assert.Same(termination, client.Operations[0].GetArgument<TerminateInstanceOptions>("options"));
        Assert.Same(purgeOptions, client.Operations[1].GetArgument<PurgeInstanceOptions>("options"));
        Assert.Same(filter, client.Operations[2].GetArgument<PurgeInstancesFilter>("filter"));
        Assert.Null(client.Operations[2].InstanceId);
        Assert.True(client.Operations[3].GetArgument<bool>("restartWithNewInstanceId"));
        Assert.Equal("dependency repaired", client.Operations[4].GetArgument<string>("reason"));
        Assert.Throws<KeyNotFoundException>(() => client.Operations[4].GetArgument<string>("unknown"));
        Assert.Throws<InvalidOperationException>(() => client.Operations[4].GetArgument<int>("reason"));
    }

    [Theory]
    [InlineData(DurableClientOperation.Terminate)]
    [InlineData(DurableClientOperation.PurgeInstance)]
    [InlineData(DurableClientOperation.PurgeAll)]
    [InlineData(DurableClientOperation.Restart)]
    [InlineData(DurableClientOperation.Rewind)]
    [InlineData(DurableClientOperation.GetHistory)]
    public async Task Missing_management_handlers_fail_explicitly_after_recording(DurableClientOperation operation)
    {
        await using var client = new TestDurableTaskClient();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(client, operation, "id", TestContext.Current.CancellationToken));
        Assert.Contains("Configure TestDurableTaskClient.", error.Message);
        client.Should().HaveOperation(operation).HaveCount(1);
    }

    [Theory]
    [InlineData(DurableClientOperation.Terminate)]
    [InlineData(DurableClientOperation.PurgeInstance)]
    [InlineData(DurableClientOperation.PurgeAll)]
    [InlineData(DurableClientOperation.Restart)]
    [InlineData(DurableClientOperation.Rewind)]
    [InlineData(DurableClientOperation.GetHistory)]
    public async Task Pre_canceled_management_calls_are_not_recorded(DurableClientOperation operation)
    {
        await using var client = new TestDurableTaskClient();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeAsync(client, operation, "id", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        client.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_management_arguments_are_not_recorded()
    {
        await using var client = new TestDurableTaskClient();
        var token = TestContext.Current.CancellationToken;
        foreach (var operation in new[] { DurableClientOperation.Terminate, DurableClientOperation.PurgeInstance,
            DurableClientOperation.Restart, DurableClientOperation.Rewind, DurableClientOperation.GetHistory })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => InvokeAsync(client, operation, " ", token));
        }

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.PurgeAllInstancesAsync(null!, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.RewindInstanceAsync("id", null!, token));
        client.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_failures_and_inflight_calls_remain_in_invocation_order()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("backend failed");
        await using var client = new TestDurableTaskClient
        {
            TerminateHandler = async (_, _, cancellation) =>
            {
                started.SetResult();
                await release.Task.WaitAsync(cancellation);
                throw failure;
            },
            RestartHandler = (_, _, _) => Task.FromResult("restarted")
        };
        var token = TestContext.Current.CancellationToken;
        var pending = client.TerminateInstanceAsync("id", token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal("restarted", await client.RestartAsync("id", cancellation: token));
        client.Should().HaveOperationSequence(DurableClientOperation.Terminate, DurableClientOperation.Restart);
        release.SetResult();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => pending));
        client.Should().HaveCount(2);
        client.RestartHandler = (_, _, _) => Task.FromResult("");
        await Assert.ThrowsAsync<ArgumentException>(() => client.RestartAsync("id", cancellation: token));
    }

    [Fact]
    public async Task Sdk_overloads_record_once_and_preserve_implicit_options()
    {
        await using var client = new TestDurableTaskClient
        {
            TerminateHandler = (_, _, _) => Task.CompletedTask,
            PurgeHandler = (_, _, _) => Task.FromResult(new PurgeResult(0)),
            PurgeAllHandler = (_, _, _) => Task.FromResult(new PurgeResult(0))
        };
        DurableTaskClient sdk = client;
        var token = TestContext.Current.CancellationToken;
        var output = new object();
        await sdk.TerminateInstanceAsync("id", output, token);
        await sdk.TerminateInstanceAsync("id", token);
        await sdk.PurgeInstanceAsync("id", token);
        await sdk.PurgeAllInstancesAsync(new PurgeInstancesFilter(null, null, null), token);
        client.Should().HaveCount(4);
        Assert.Same(output, client.Operations[0].GetArgument<TerminateInstanceOptions>("options")!.Output);
        Assert.Null(client.Operations[1].GetArgument<TerminateInstanceOptions>("options"));
        Assert.Null(client.Operations[2].GetArgument<PurgeInstanceOptions>("options"));
    }

    #region docs-functions-durable-client-diagnostics

    [Fact]
    public async Task Scenario_failure_attaches_timeline_before_resetting_client_state()
    {
        await using var client = new TestDurableTaskClient
        {
            RaiseEventHandler = (_, _, _, _) => Task.CompletedTask
        };
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .UseScenarioResource("Durable", client)
            .Build();
        var error = await Assert.ThrowsAsync<TestDurableTaskClientVerificationException>(() =>
            host.RunInTestScenarioScopeAsync(async (_, token) =>
            {
                client.WithInstance(new TestOrchestrationMetadataBuilder("Order", "order-42").Build());
                await client.RaiseEventAsync("order-42", "Approved", new { Secret = "private-payload" }, token);
                client.Should().HaveRaisedEvent("order-42", "Rejected");
            }, TestContext.Current.CancellationToken));

        var diagnostics = Assert.IsType<TestScenarioDiagnostics>(error.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        var json = JsonSerializer.Serialize(diagnostics.Resources["Durable"]);
        Assert.Contains("RaiseEvent", json);
        Assert.Contains("Approved", json);
        Assert.Contains("order-42", json);
        Assert.DoesNotContain("private-payload", json);
        Assert.Contains("Recorded attempts:", error.Message);
        client.Should().BeEmpty();
        Assert.Null(await client.GetInstanceAsync("order-42", TestContext.Current.CancellationToken));
    }

    #endregion

    private static async Task InvokeAsync(TestDurableTaskClient client, DurableClientOperation operation, string instanceId, CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case DurableClientOperation.Terminate:
                await client.TerminateInstanceAsync(instanceId, cancellationToken);
                break;
            case DurableClientOperation.PurgeInstance:
                await client.PurgeInstanceAsync(instanceId, cancellationToken);
                break;
            case DurableClientOperation.PurgeAll:
                await client.PurgeAllInstancesAsync(new PurgeInstancesFilter(null, null, null), cancellationToken);
                break;
            case DurableClientOperation.Restart:
                await client.RestartAsync(instanceId, cancellation: cancellationToken);
                break;
            case DurableClientOperation.Rewind:
                await client.RewindInstanceAsync(instanceId, "fixed", cancellationToken);
                break;
            case DurableClientOperation.GetHistory:
                await client.GetOrchestrationHistoryAsync(instanceId, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
