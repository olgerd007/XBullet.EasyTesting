using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class ConnectedDurableWorkflowSerializationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    #region docs-functions-connected-serialization

    [Fact]
    public async Task Serialization_converts_payload_shapes_and_returns_independent_results()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddOrchestrator<WorkflowFunction, Payload>("Order", async (_, context, _) =>
            {
                var input = context.GetInput<Payload>();
                return await context.CallActivityAsync<Payload>("Increment", new { input.Value });
            })
            .AddActivity<WorkflowFunction, Payload, object>("Increment", (_, input, _) =>
                Task.FromResult<object>(new { Value = input.Value + 1 }));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", new { Value = 2 }, token);

        var result = await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token);
        Assert.Equal(3, result.Value);
        result.Value = 99;
        Assert.Equal(3, (await workflow.GetResultAsync<Payload>(id, token)).Value);
        var metadata = await workflow.Client.GetInstanceAsync(id, true, token);
        Assert.Equal(2, metadata!.ReadInputAs<Payload>()!.Value);
        Assert.Equal(3, metadata.ReadOutputAs<Payload>()!.Value);
    }

    public sealed class Payload
    {
        public int Value { get; set; }
    }

    public sealed class WorkflowFunction;

    #endregion

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduling_snapshots_inputs_and_result_reads_only_when_enabled(bool serialize)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new Payload { Value = 4 };
        Payload? returned = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddOrchestrator<WorkflowFunction, Payload>("Order", async (_, context, worker) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(worker.CancellationToken);
                returned = context.GetInput<Payload>();
                var again = context.GetInput<Payload>();
                Assert.Equal(!serialize, ReferenceEquals(returned, again));
                return returned;
            });
        if (serialize)
        {
            workflow.WithSerialization();
        }

        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", input, token);
        await entered.Task.WaitAsync(Timeout, token);
        input.Value = 8;
        release.SetResult();
        var result = await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token);
        Assert.Equal(serialize ? 4 : 8, result.Value);
        Assert.Equal(!serialize, ReferenceEquals(returned, result));
        result.Value = 12;
        Assert.Equal(serialize ? 4 : 12, (await workflow.GetResultAsync<Payload>(id, token)).Value);
        Assert.Same(input, Assert.Single(workflow.Client.ScheduledOrchestrations).Input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activity_boundaries_isolate_both_directions_only_when_enabled(bool serialize)
    {
        Payload? activityInput = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().UseHost(host)
            .AddActivity<WorkflowFunction, Payload, Payload>("Change", (_, input, worker) =>
            {
                Assert.Same(input, worker.Bindings.GetInput<Payload>("input"));
                activityInput = input;
                input.Value = 9;
                return Task.FromResult(input);
            })
            .AddOrchestrator<WorkflowFunction, Payload>("Order", async (_, context, _) =>
            {
                var input = new Payload { Value = 1 };
                var result = await context.CallActivityAsync<Payload>("Change", input);
                Assert.Equal(serialize ? 1 : 9, input.Value);
                Assert.Equal(!serialize, ReferenceEquals(activityInput, result));
                result.Value = 7;
                Assert.Equal(serialize ? 9 : 7, activityInput!.Value);
                Assert.Same(input, Assert.Single(context.ActivityCalls).Input);
                return result;
            });
        if (serialize)
        {
            workflow.WithSerialization();
        }

        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Equal(7, (await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token)).Value);
    }

    [Fact]
    public async Task Child_inputs_and_results_deserialize_to_each_receivers_requested_type()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddOrchestrator<WorkflowFunction, Payload>("Parent", async (_, context, _) =>
                await context.CallSubOrchestratorAsync<Payload>("Child", new { Value = 6 }))
            .AddOrchestrator<WorkflowFunction, object>("Child", (_, context, _) =>
                Task.FromResult<object>(new { Value = context.GetInput<Payload>().Value + 1 }));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Parent", token);
        Assert.Equal(7, (await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token)).Value);
        var child = await workflow.Client.GetInstanceAsync($"{id}:child:1", true, token);
        Assert.Equal(6, child!.ReadInputAs<Payload>()!.Value);
        Assert.Equal(7, child.ReadOutputAs<Payload>()!.Value);
    }

    [Fact]
    public async Task Buffered_client_events_and_direct_context_events_snapshot_at_delivery()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Order", async (_, context, worker) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(worker.CancellationToken);
                var first = await context.WaitForExternalEvent<Payload>("Approved");
                var second = await context.WaitForExternalEvent<Payload>("Direct");
                context.SendEvent(context.InstanceId, "Self", new { Value = 3 });
                var self = await context.WaitForExternalEvent<Payload>("Self");
                return first.Value + second.Value + self.Value;
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await entered.Task.WaitAsync(Timeout, token);
        var input = new Payload { Value = 1 };
        await workflow.Client.RaiseEventAsync(id, "Approved", input, token);
        workflow.GetContext(id).RaiseEvent("Direct", new { Value = 2 });
        input.Value = 90;
        release.SetResult();
        Assert.Equal(6, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        Assert.Same(input, workflow.Client.Operations.Single(call => call.Operation == DurableClientOperation.RaiseEvent).GetArgument<object>("eventPayload"));
    }

    [Fact]
    public async Task Custom_converter_is_shared_with_metadata_without_double_encoding_snapshots()
    {
        var converter = new PrefixedConverter();
        var status = new Payload { Value = 11 };
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization(converter).UseHost(host)
            .AddOrchestrator<WorkflowFunction, Payload>("Order", async (_, context, _) =>
            {
                context.SetCustomStatus(status);
                return await context.CallSubOrchestratorAsync<Payload>("Child", context.GetInput<Payload>());
            })
            .AddOrchestrator<WorkflowFunction, Payload>("Child", (_, context, _) => context.CallActivityAsync<Payload>("Echo", context.GetInput<Payload>()))
            .AddActivity<WorkflowFunction, Payload, Payload>("Echo", (_, input, _) => Task.FromResult(input));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", new { Value = 5 }, token);
        Assert.Equal(5, (await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token)).Value);
        status.Value = 99;
        converter.RejectSerialization = true;
        var metadata = await workflow.Client.GetInstanceAsync(id, true, token);
        Assert.Same(converter, metadata!.DataConverter);
        Assert.Equal("encoded:{\"Value\":5}", metadata.SerializedInput);
        Assert.Equal("encoded:{\"Value\":5}", metadata.SerializedOutput);
        Assert.Equal(5, metadata.ReadOutputAs<Payload>()!.Value);
        Assert.Equal(11, metadata.ReadCustomStatusAs<Payload>()!.Value);
        var excluded = await workflow.Client.GetInstanceAsync(id, false, token);
        Assert.Null(excluded!.SerializedInput);
        Assert.Null(excluded.DataConverter);
    }

    [Fact]
    public async Task Failed_input_serialization_leaves_no_instance_and_configuration_remains_editable()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Order", (_, _, _) => Task.FromResult(42));
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<NotSupportedException>(() => workflow.Client.ScheduleNewOrchestrationInstanceAsync(
            "Order", new { Callback = (Action)(() => { }) }, new() { InstanceId = "bad" }, token));
        Assert.Null(await workflow.Client.GetInstanceAsync("bad", token));
        workflow.WithSerialization();
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Equal(42, await workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
    }

    [Theory]
    [InlineData("OrchestratorInput")]
    [InlineData("ActivityInput")]
    [InlineData("ActivityResult")]
    [InlineData("ChildResult")]
    [InlineData("Event")]
    public async Task Incompatible_payloads_fail_receivers_and_set_failed_metadata(string boundary)
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddActivity<WorkflowFunction, int, string>("Text", (_, _, _) => Task.FromResult("wrong type"))
            .AddOrchestrator<WorkflowFunction, string>("Child", (_, _, _) => Task.FromResult("wrong type"))
            .AddOrchestrator<WorkflowFunction, int>("Order", async (_, context, _) => boundary switch
            {
                "OrchestratorInput" => context.GetInput<int>(),
                "ActivityInput" => await context.CallActivityAsync<int>("Text", "wrong type"),
                "ActivityResult" => await context.CallActivityAsync<int>("Text", 1),
                "ChildResult" => await context.CallSubOrchestratorAsync<int>("Child"),
                _ => await context.WaitForExternalEvent<int>("Number")
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", "wrong type", token);
        if (boundary == "Event")
        {
            await workflow.Client.RaiseEventAsync(id, "Number", "wrong type", token);
        }

        await Assert.ThrowsAsync<JsonException>(() => workflow.GetResultAsync<int>(id, token).WaitAsync(Timeout, token));
        var metadata = await workflow.Client.GetInstanceAsync(id, false, token);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata!.RuntimeStatus);
        Assert.Null(metadata.FailureDetails);
    }

    [Fact]
    public async Task Output_serialization_failure_closes_activity_scope_and_cleanup_can_drain()
    {
        ConnectedDurableWorkflowTests.ScopeMarker? scope = null;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().AddFunction<ScopedActivity>()
            .ConfigureServices(services => services.AddScoped<ConnectedDurableWorkflowTests.ScopeMarker>()).Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddActivity<ScopedActivity, int, object>("BadOutput", (function, _, _) =>
            {
                scope = function.Scope;
                return Task.FromResult<object>((Action)(() => { }));
            })
            .AddOrchestrator<WorkflowFunction, object>("Order", (_, context, _) => context.CallActivityAsync<object>("BadOutput", 1));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await Assert.ThrowsAsync<NotSupportedException>(() => workflow.GetResultAsync<object>(id, token).WaitAsync(Timeout, token));
        Assert.True(scope!.Disposed);
        await workflow.ResetAsync(token);
        Assert.Empty(workflow.Client.Operations);
    }

    [Theory]
    [InlineData("ActivityInput")]
    [InlineData("ChildInput")]
    [InlineData("OrchestratorOutput")]
    [InlineData("CustomStatus")]
    public async Task Unsupported_payloads_fail_at_submission_or_output_and_leave_no_active_activities(string boundary)
    {
        var invoked = false;
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddActivity<WorkflowFunction, int, int>("Number", (_, input, _) =>
            {
                invoked = true;
                return Task.FromResult(input);
            })
            .AddOrchestrator<WorkflowFunction, int>("Child", (_, _, _) =>
            {
                invoked = true;
                return Task.FromResult(1);
            })
            .AddOrchestrator<WorkflowFunction, object>("Order", async (_, context, _) =>
            {
                var unsupported = (Action)(() => { });
                if (boundary == "ActivityInput")
                {
                    return await context.CallActivityAsync<int>("Number", unsupported);
                }
                if (boundary == "ChildInput")
                {
                    return await context.CallSubOrchestratorAsync<int>("Child", unsupported);
                }
                if (boundary == "CustomStatus")
                {
                    context.SetCustomStatus(unsupported);
                    return 0;
                }

                return unsupported;
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await Assert.ThrowsAsync<NotSupportedException>(() => workflow.GetResultAsync<object>(id, token).WaitAsync(Timeout, token));
        Assert.False(invoked);
        var diagnostics = JsonSerializer.SerializeToElement(await workflow.CaptureDiagnosticsAsync(token));
        Assert.Equal(0, diagnostics.GetProperty("ActiveActivities").GetInt32());
        Assert.Equal(OrchestrationRuntimeStatus.Failed, (await workflow.Client.GetInstanceAsync(id, token))!.RuntimeStatus);
        await workflow.ResetAsync(token);
    }

    [Fact]
    public async Task Unserializable_event_submission_does_not_consume_wait_and_terminal_events_are_ignored()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddOrchestrator<WorkflowFunction, int>("Order", async (_, context, _) =>
            {
                var pending = context.WaitForExternalEvent<Payload>("Number");
                ready.SetResult();
                return (await pending).Value;
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        await ready.Task.WaitAsync(Timeout, token);
        var result = workflow.GetResultAsync<int>(id, token);
        await Assert.ThrowsAsync<NotSupportedException>(() => workflow.Client.RaiseEventAsync(id, "Number", (Action)(() => { }), token));
        Assert.False(result.IsCompleted);
        await workflow.Client.RaiseEventAsync(id, "Number", new { Value = 3 }, token);
        Assert.Equal(3, await result.WaitAsync(Timeout, token));
        await workflow.Client.RaiseEventAsync(id, "Number", (Action)(() => { }), token);
    }

    [Fact]
    public async Task Configuration_freezes_on_schedule_and_reset_retains_serialization()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddOrchestrator<WorkflowFunction, Payload>("Order", (_, context, _) => Task.FromResult(context.GetInput<Payload>()));
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", new { Value = 2 }, token);
        Assert.Throws<InvalidOperationException>(() => workflow.WithSerialization());
        Assert.Equal(2, (await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token)).Value);
        await workflow.ResetAsync(token);
        id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", new { Value = 3 }, token);
        Assert.Equal(3, (await workflow.GetResultAsync<Payload>(id, token).WaitAsync(Timeout, token)).Value);
        await workflow.ResetAsync(token);
        workflow.WithSerialization(new PrefixedConverter());
    }

    [Fact]
    public async Task Null_inputs_void_functions_and_nullable_results_remain_supported()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<WorkflowFunction>().Build();
        await using var workflow = new TestDurableWorkflow().WithSerialization().UseHost(host)
            .AddActivity<WorkflowFunction, object?>("Save", (_, input, _) =>
            {
                Assert.Null(input);
                return Task.CompletedTask;
            })
            .AddOrchestrator<WorkflowFunction>("Order", async (_, context, _) =>
            {
                Assert.Equal(0, context.GetInput<int>());
                Assert.Null(context.GetInput<Payload>());
                await context.CallActivityAsync("Save");
            });
        var token = TestContext.Current.CancellationToken;
        var id = await workflow.Client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Null(await workflow.GetResultAsync<int?>(id, token).WaitAsync(Timeout, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GetResultAsync<int>(id, token));
        Assert.Equal(OrchestrationRuntimeStatus.Completed, (await workflow.Client.GetInstanceAsync(id, token))!.RuntimeStatus);
    }

    public sealed class ScopedActivity(ConnectedDurableWorkflowTests.ScopeMarker scope)
    {
        public ConnectedDurableWorkflowTests.ScopeMarker Scope { get; } = scope;
    }

    private sealed class PrefixedConverter : DataConverter
    {
        public bool RejectSerialization { get; set; }

        public override string? Serialize(object? value)
        {
            if (RejectSerialization)
            {
                throw new InvalidOperationException("Unexpected serialization after the snapshot.");
            }

            var json = JsonDataConverter.Default.Serialize(value);
            return json is null ? null : "encoded:" + json;
        }

        public override object? Deserialize(string? data, Type targetType)
        {
            if (data is not null && !data.StartsWith("encoded:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The metadata did not preserve its configured converter.");
            }

            return JsonDataConverter.Default.Deserialize(data?[8..], targetType);
        }
    }
}
