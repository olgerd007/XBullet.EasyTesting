using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Http;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableMetadataTests
{
    #region docs-functions-durable-metadata

    [Fact]
    public async Task Seeded_metadata_supports_status_endpoints_and_typed_payload_reads()
    {
        var metadata = new TestOrchestrationMetadataBuilder("Order", "order-42")
            .WithRuntimeStatus(OrchestrationRuntimeStatus.Completed)
            .WithInput(new OrderInput(42))
            .WithOutput("accepted")
            .WithCustomStatus(new { Step = "finished" })
            .Build();
        await using var client = new TestDurableTaskClient().WithInstance(metadata);
        var token = TestContext.Current.CancellationToken;

        var summary = await client.GetInstanceAsync("order-42", token);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, summary!.RuntimeStatus);
        Assert.Null(summary.SerializedInput);
        Assert.Throws<InvalidOperationException>(() => summary.ReadInputAs<OrderInput>());

        var details = await client.GetInstanceAsync("order-42", getInputsAndOutputs: true, cancellation: token);
        Assert.Equal(new OrderInput(42), details!.ReadInputAs<OrderInput>());
        Assert.Equal("accepted", details.ReadOutputAs<string>());
        Assert.Equal("finished", details.ReadCustomStatusAs<JsonElement>().GetProperty("Step").GetString());
        client.Should().HaveOperation(DurableClientOperation.GetInstance, "order-42", times: 2);
    }

    #endregion

    [Fact]
    public async Task Seeded_completion_drives_the_real_sdk_http_completion_helper()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().Build();
        await using var client = new TestDurableTaskClient().WithInstance(
            new TestOrchestrationMetadataBuilder("Order", "order-42")
                .WithRuntimeStatus(OrchestrationRuntimeStatus.Completed)
                .WithOutput("accepted")
                .Build());
        var token = TestContext.Current.CancellationToken;
        var request = host.HttpRequest("Status", token).Build();
        var response = await client.WaitForCompletionOrCreateCheckStatusResponseAsync(
            request, "order-42", getInputsAndOutputs: true, cancellation: token);
        var payload = await response.ReadBodyAsJsonAsync<JsonElement>(cancellationToken: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", payload.GetProperty("runtimeStatus").GetString());
        Assert.Equal("\"accepted\"", payload.GetProperty("serializedOutput").GetString());
        client.Should().HaveOperation(DurableClientOperation.GetInstance, "order-42");
    }

    [Theory]
    [InlineData(OrchestrationRuntimeStatus.Pending)]
    [InlineData(OrchestrationRuntimeStatus.Running)]
    [InlineData(OrchestrationRuntimeStatus.Completed)]
    [InlineData(OrchestrationRuntimeStatus.Failed)]
    [InlineData(OrchestrationRuntimeStatus.Terminated)]
    [InlineData(OrchestrationRuntimeStatus.Suspended)]
    public void Builder_reports_explicit_status_and_deterministic_defaults(OrchestrationRuntimeStatus status)
    {
        var metadata = new TestOrchestrationMetadataBuilder("Order", "id").WithRuntimeStatus(status).Build();
        Assert.Equal(status, metadata.RuntimeStatus);
        Assert.Equal(DateTimeOffset.UnixEpoch, metadata.CreatedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch, metadata.LastUpdatedAt);
        Assert.Null(metadata.ReadInputAs<object>());
        Assert.Null(metadata.ReadOutputAs<object>());
        Assert.Null(metadata.ReadCustomStatusAs<object>());
        Assert.Empty(metadata.Tags);
    }

    [Fact]
    public void Builder_serializes_at_build_time_with_the_chosen_converter_and_copies_tags()
    {
        var input = new List<string> { "first" };
        var tags = new Dictionary<string, string> { ["tenant"] = "north" };
        var creation = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(3));
        var builder = new TestOrchestrationMetadataBuilder("Order", "id")
            .WithInput(input)
            .WithCustomStatus(new OrderInput(42))
            .WithDataConverter(new JsonDataConverter(new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .WithTags(tags)
            .WithTimestamps(creation, creation.AddMinutes(5));
        tags["tenant"] = "changed";
        var first = builder.Build();
        input.Add("second");
        var second = builder.Build();

        Assert.Equal(["first"], first.ReadInputAs<List<string>>());
        Assert.Equal(["first", "second"], second.ReadInputAs<List<string>>());
        Assert.Equal("north", first.Tags["tenant"]);
        Assert.NotSame(first.Tags, second.Tags);
        Assert.Equal(TimeSpan.Zero, first.CreatedAt.Offset);
        Assert.Equal(creation.ToUniversalTime(), first.CreatedAt);
        Assert.Equal(creation.AddMinutes(5).ToUniversalTime(), first.LastUpdatedAt);
        Assert.Contains("\"orderId\"", first.SerializedCustomStatus);
        Assert.Equal(new OrderInput(42), first.ReadCustomStatusAs<OrderInput>());
    }

    [Fact]
    public async Task Failure_details_are_available_only_when_payloads_are_requested()
    {
        var failure = new InvalidOperationException("outer", new ArgumentException("inner"));
        var builder = new TestOrchestrationMetadataBuilder("Order", "id").WithFailure(failure);
        var metadata = builder.Build();
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Equal(typeof(InvalidOperationException).FullName, metadata.FailureDetails!.ErrorType);
        Assert.Equal("inner", metadata.FailureDetails.InnerFailure!.ErrorMessage);
        await using var client = new TestDurableTaskClient().WithInstance(metadata);
        var token = TestContext.Current.CancellationToken;
        Assert.Null((await client.GetInstanceAsync("id", token))!.FailureDetails);
        Assert.Same(metadata.FailureDetails, (await client.GetInstanceAsync("id", true, token))!.FailureDetails);
        Assert.Null(builder.WithRuntimeStatus(OrchestrationRuntimeStatus.Completed).Build().FailureDetails);
    }

    [Fact]
    public async Task Seed_replacement_handler_precedence_and_management_preserve_explicit_state()
    {
        var token = TestContext.Current.CancellationToken;
        var builder = new TestOrchestrationMetadataBuilder("Order", "id");
        await using var client = new TestDurableTaskClient().WithInstance(builder.Build());
        client.WithInstance(builder.WithRuntimeStatus(OrchestrationRuntimeStatus.Completed).Build());
        Assert.Equal(OrchestrationRuntimeStatus.Completed, (await client.GetInstanceAsync("id", token))!.RuntimeStatus);
        Assert.Null(await client.GetInstanceAsync("ID", token));
        Assert.Null(await client.GetInstanceAsync("missing", token));
        client.GetInstanceHandler = (_, _, _) => Task.FromResult<OrchestrationMetadata?>(null);
        Assert.Null(await client.GetInstanceAsync("id", token));
        client.GetInstanceHandler = null;
        client.TerminateHandler = (_, _, _) => Task.CompletedTask;
        await client.TerminateInstanceAsync("id", token);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, (await client.GetInstanceAsync("id", token))!.RuntimeStatus);
        var generated = await client.ScheduleNewOrchestrationInstanceAsync("Order", token);
        Assert.Null(await client.GetInstanceAsync(generated, token));
    }

    [Fact]
    public async Task Seeding_and_reads_copy_mutable_tags_and_metadata_containers()
    {
        var tags = new Dictionary<string, string> { ["region"] = "west" };
        var metadata = new OrchestrationMetadata("Order", "id") { Tags = tags };
        await using var client = new TestDurableTaskClient().WithInstance(metadata);
        tags["region"] = "east";
        var first = await client.GetInstanceAsync("id", TestContext.Current.CancellationToken);
        var second = await client.GetInstanceAsync("id", TestContext.Current.CancellationToken);
        Assert.NotSame(metadata, first);
        Assert.NotSame(first, second);
        Assert.Equal("west", first!.Tags["region"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)first.Tags)["region"] = "changed");
    }

    [Fact]
    public void Builder_validates_identity_status_converter_and_time_order()
    {
        Assert.Throws<ArgumentException>(() => new TestOrchestrationMetadataBuilder(" ", "id"));
        Assert.Throws<ArgumentNullException>(() => new TestOrchestrationMetadataBuilder("Order", null!));
        var builder = new TestOrchestrationMetadataBuilder("Order", "id");
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithRuntimeStatus((OrchestrationRuntimeStatus)999));
        Assert.Throws<ArgumentNullException>(() => builder.WithDataConverter(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithFailure((Exception)null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithFailure((TaskFailureDetails)null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithTags(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimestamps(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(-1)));
    }

    private sealed record OrderInput(int OrderId);
}
