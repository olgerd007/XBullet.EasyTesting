using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Http;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableClientTests
{
    #region docs-functions-durable-client

    [Fact]
    public async Task Http_starter_records_scheduling_and_returns_sdk_management_response()
    {
        var builder = AzureFunctionTestHost.CreateBuilder();
        builder.AddFunction<StartOrderOrchestrationFunction>();
        await using var host = builder.Build();
        await using var client = new TestDurableTaskClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = host.HttpRequest(nameof(StartOrderOrchestrationFunction), cancellationToken)
            .WithMethod(HttpMethod.Post)
            .WithUrl("https://example.test/api/orders")
            .WithTextBody("order input")
            .Build();

        var invocation = await host.InvokeAsync<StartOrderOrchestrationFunction, HttpResponseData>(
            (TestFunctionContext)request.FunctionContext,
            (function, context) => function.RunAsync(request, client, context.CancellationToken));
        var response = Assert.IsAssignableFrom<HttpResponseData>(invocation.Result);
        var payload = await response.ReadBodyAsJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        Assert.True(invocation.FunctionExecuted);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("order-42", payload.GetProperty("id").GetString());
        Assert.Equal(
            payload.GetProperty("statusQueryGetUri").GetString(),
            Assert.Single(response.Headers.GetValues("Location")));
        Assert.StartsWith("https://example.test/runtime/webhooks/durabletask/instances/order-42",
            payload.GetProperty("statusQueryGetUri").GetString());
        var call = Assert.Single(client.ScheduledOrchestrations);
        Assert.Equal("ProcessOrder", call.OrchestratorName.Name);
        Assert.Equal("order input", call.Input);
        Assert.Equal("order-42", call.InstanceId);
    }

    #endregion

    [Fact]
    public async Task Synchronous_sdk_response_escapes_instance_ids_and_honors_status_code()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().Build();
        await using var client = new TestDurableTaskClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = host.HttpRequest("Starter", cancellationToken)
            .WithUrl("https://example.test/api/start")
            .Build();

        var response = client.CreateCheckStatusResponse(request, "order /?42", HttpStatusCode.Created, cancellationToken);
        var payload = await response.ReadBodyAsJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("order /?42", payload.GetProperty("id").GetString());
        Assert.Contains("order%20%2F%3F42", payload.GetProperty("statusQueryGetUri").GetString());
    }

    [Fact]
    public async Task Scheduling_overloads_preserve_input_options_and_generate_repeatable_ids()
    {
        await using var client = new TestDurableTaskClient("starter");
        DurableTaskClient sdkClient = client;
        var cancellationToken = TestContext.Current.CancellationToken;
        var input = new { OrderId = 42 };
        var options = new StartOrchestrationOptions { InstanceId = "explicit", Version = "v2" };

        Assert.Equal("test-instance-1", await sdkClient.ScheduleNewOrchestrationInstanceAsync("Order", cancellationToken));
        Assert.Equal("test-instance-2", await sdkClient.ScheduleNewOrchestrationInstanceAsync("Order", input, cancellationToken));
        Assert.Equal("explicit", await sdkClient.ScheduleNewOrchestrationInstanceAsync("Order", options, cancellationToken));
        Assert.Equal("explicit", await sdkClient.ScheduleNewOrchestrationInstanceAsync("Order", input, options, cancellationToken));

        Assert.Equal("starter", client.Name);
        Assert.Equal(4, client.ScheduledOrchestrations.Count);
        Assert.Same(input, client.ScheduledOrchestrations[3].Input);
        Assert.Same(options, client.ScheduledOrchestrations[3].Options);
        await using var other = new TestDurableTaskClient();
        Assert.Equal("test-instance-1", await other.ScheduleNewOrchestrationInstanceAsync("Order", cancellationToken));
    }

    [Fact]
    public async Task Scheduling_handler_receives_token_and_records_failed_attempts()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("scheduler unavailable");
        await using var client = new TestDurableTaskClient
        {
            ScheduleHandler = (call, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                Assert.Equal("Order", call.OrchestratorName.Name);
                return Task.FromException<string>(failure);
            }
        };

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ScheduleNewOrchestrationInstanceAsync("Order", cancellation.Token)));
        Assert.Single(client.ScheduledOrchestrations);
        client.ScheduleHandler = (_, _) => Task.FromResult("custom-id");
        Assert.Equal("custom-id", await client.ScheduleNewOrchestrationInstanceAsync("Order", cancellation.Token));
        client.ScheduleHandler = (_, _) => Task.FromResult("");
        await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleNewOrchestrationInstanceAsync("Order", cancellation.Token));
    }

    [Fact]
    public async Task Pre_canceled_and_invalid_starts_are_not_recorded()
    {
        await using var client = new TestDurableTaskClient();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ScheduleNewOrchestrationInstanceAsync("Order", cancellation.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ScheduleNewOrchestrationInstanceAsync(default, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ScheduleNewOrchestrationInstanceAsync(
            "Order", null, new StartOrchestrationOptions { InstanceId = " " }, TestContext.Current.CancellationToken));
        Assert.Empty(client.ScheduledOrchestrations);
        Assert.Throws<ArgumentException>(() => new TestDurableTaskClient(" "));
    }

    [Fact]
    public async Task Concurrent_scheduling_returns_distinct_ids_and_collection_snapshots()
    {
        await using var client = new TestDurableTaskClient();
        var snapshot = client.ScheduledOrchestrations;
        var cancellationToken = TestContext.Current.CancellationToken;
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            Task.Run(() => client.ScheduleNewOrchestrationInstanceAsync("Order", cancellationToken), cancellationToken)));
        Assert.Equal(100, results.Distinct().Count());
        Assert.Equal(100, client.ScheduledOrchestrations.Count);
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task Metadata_and_management_handlers_receive_original_arguments()
    {
        await using var client = new TestDurableTaskClient();
        Assert.Null(await client.GetInstanceAsync("missing", TestContext.Current.CancellationToken));
        var metadata = new OrchestrationMetadata("Order", "id") { RuntimeStatus = OrchestrationRuntimeStatus.Completed };
        var cancellationToken = TestContext.Current.CancellationToken;
        client.GetInstanceHandler = (id, include, token) =>
        {
            Assert.Equal("id", id);
            Assert.True(include);
            Assert.Equal(cancellationToken, token);
            return Task.FromResult<OrchestrationMetadata?>(metadata);
        };
        Assert.Same(metadata, await client.GetInstanceAsync("id", true, cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RaiseEventAsync("id", "Approved", cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.WaitForInstanceStartAsync("id", cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.WaitForInstanceCompletionAsync("id", cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SuspendInstanceAsync("id", cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ResumeInstanceAsync("id", cancellationToken));
        Assert.Throws<InvalidOperationException>(() => client.GetAllInstancesAsync());

        var payload = new object();
        client.RaiseEventHandler = (id, name, value, token) =>
        {
            Assert.Equal("id", id);
            Assert.Equal("Approved", name);
            Assert.Same(payload, value);
            Assert.Equal(cancellationToken, token);
            return Task.CompletedTask;
        };
        await client.RaiseEventAsync("id", "Approved", payload, cancellationToken);
        client.WaitForStartHandler = (_, _, _) => Task.FromResult(metadata);
        client.WaitForCompletionHandler = (_, _, _) => Task.FromResult(metadata);
        Assert.Same(metadata, await client.WaitForInstanceStartAsync("id", cancellationToken));
        Assert.Same(metadata, await client.WaitForInstanceCompletionAsync("id", cancellationToken));
        client.SuspendHandler = (id, reason, token) =>
        {
            Assert.Equal("id", id);
            Assert.Equal("maintenance", reason);
            Assert.Equal(cancellationToken, token);
            return Task.CompletedTask;
        };
        client.ResumeHandler = client.SuspendHandler;
        await client.SuspendInstanceAsync("id", "maintenance", cancellationToken);
        await client.ResumeInstanceAsync("id", "maintenance", cancellationToken);

        var query = new OrchestrationQuery();
        var pageable = Pageable.Create<OrchestrationMetadata>((_, token) =>
        {
            Assert.Equal(cancellationToken, token);
            return Task.FromResult(new Page<OrchestrationMetadata>([metadata], null));
        });
        client.QueryHandler = filter =>
        {
            Assert.Same(query, filter);
            return pageable;
        };
        await foreach (var item in client.GetAllInstancesAsync(query).WithCancellation(cancellationToken))
        {
            Assert.Same(metadata, item);
        }
    }

    [Fact]
    public async Task Pre_canceled_management_calls_do_not_invoke_handlers()
    {
        await using var client = new TestDurableTaskClient();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetInstanceAsync("id", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RaiseEventAsync("id", "Approved", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WaitForInstanceStartAsync("id", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WaitForInstanceCompletionAsync("id", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SuspendInstanceAsync("id", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResumeInstanceAsync("id", cancellation.Token));
    }
}
