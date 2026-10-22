using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableClientAssertionTests
{
    [Fact]
    public async Task Assertions_match_counts_targets_payload_predicates_and_exact_order()
    {
        await using var client = new TestDurableTaskClient { RaiseEventHandler = (_, _, _, _) => Task.CompletedTask };
        var token = TestContext.Current.CancellationToken;
        var id = await client.ScheduleNewOrchestrationInstanceAsync("Order", new { Quantity = 3 }, token);
        await client.RaiseEventAsync(id, "Approved", true, token);
        await client.RaiseEventAsync(id, "approved", false, token);
        client.Should().HaveCount(3)
            .HaveScheduledOrchestration("Order", id)
            .HaveScheduledOrchestration("order", times: 0)
            .HaveRaisedEvent(id, "APPROVED", times: 2)
            .HaveRaisedEvent(id.ToUpperInvariant(), "Approved", times: 0)
            .HaveMatchingOperations(call => call.Operation == DurableClientOperation.RaiseEvent && call.GetArgument<bool>("eventPayload"))
            .HaveOperationSequence(DurableClientOperation.Schedule, DurableClientOperation.RaiseEvent, DurableClientOperation.RaiseEvent);
        Assert.Equal(3, client.Operations.Count);
    }

    [Fact]
    public async Task Assertion_failures_show_expectations_and_order_without_payloads()
    {
        var failure = new InvalidOperationException("sensitive failure");
        await using var client = new TestDurableTaskClient { ScheduleHandler = (_, _) => Task.FromException<string>(failure) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ScheduleNewOrchestrationInstanceAsync(
            "Order", new { Password = "sensitive-payload" }, TestContext.Current.CancellationToken));
        var error = Assert.Throws<TestDurableTaskClientVerificationException>(() => client.Should().HaveScheduledOrchestration("Other"));
        Assert.Contains("Expected 1 scheduling attempt(s) for 'Other'", error.Message);
        Assert.Contains("1. Schedule", error.Message);
        Assert.Contains("Order", error.Message);
        Assert.DoesNotContain("sensitive-payload", error.Message);
        Assert.DoesNotContain("sensitive failure", error.Message);
        client.Should().HaveScheduledOrchestration("Order");
        Assert.Throws<TestDurableTaskClientVerificationException>(() => client.Should().HaveCount(0));
        Assert.Throws<TestDurableTaskClientVerificationException>(() => client.Should().HaveOperationSequence(DurableClientOperation.GetInstance));
        Assert.Throws<TestDurableTaskClientVerificationException>(() => client.Should().HaveOperation(DurableClientOperation.Terminate));
        await using var empty = new TestDurableTaskClient();
        var emptyError = Assert.Throws<TestDurableTaskClientVerificationException>(() => empty.Should().HaveCount(1));
        Assert.Contains("<none>", emptyError.Message);
    }

    [Fact]
    public async Task Reset_clears_fixtures_and_sequences_preserves_handlers_and_detaches_diagnostics()
    {
        await using var client = new TestDurableTaskClient { RaiseEventHandler = (_, _, _, _) => Task.CompletedTask };
        var token = TestContext.Current.CancellationToken;
        client.WithInstance(new TestOrchestrationMetadataBuilder("Order", "id").WithInput("private-input").Build());
        await client.RaiseEventAsync("id", "Approved", new { Self = client }, token);
        var operations = client.Operations;
        var diagnostics = await client.CaptureDiagnosticsAsync(token);
        var json = JsonSerializer.Serialize(diagnostics);
        Assert.DoesNotContain("private-input", json);
        await client.ResetAsync(token);
        client.Should().BeEmpty();
        Assert.Single(operations);
        Assert.Equal(json, JsonSerializer.Serialize(diagnostics));
        Assert.Null(await client.GetInstanceAsync("id", token));
        Assert.Equal("test-instance-1", await client.ScheduleNewOrchestrationInstanceAsync("Order", token));
        Assert.Equal(1, client.Operations[0].Sequence);
        await client.RaiseEventAsync("id", "Approved", token);
        Assert.Equal(3, client.Operations.Count);
    }

    [Fact]
    public async Task Canceled_reset_and_capture_leave_state_untouched()
    {
        await using var client = new TestDurableTaskClient();
        await client.ScheduleNewOrchestrationInstanceAsync("Order", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.ResetAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.CaptureDiagnosticsAsync(cancellation.Token));
        client.Should().HaveCount(1);
    }

    [Fact]
    public void Assertions_validate_arguments()
    {
        var client = new TestDurableTaskClient();
        var assertions = client.Should();
        Assert.Throws<ArgumentOutOfRangeException>(() => assertions.HaveCount(-1));
        Assert.Throws<ArgumentException>(() => assertions.HaveScheduledOrchestration(" "));
        Assert.Throws<ArgumentException>(() => assertions.HaveScheduledOrchestration("Order", " "));
        Assert.Throws<ArgumentException>(() => assertions.HaveRaisedEvent(" ", "Approved"));
        Assert.Throws<ArgumentException>(() => assertions.HaveRaisedEvent("id", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => assertions.HaveRaisedEvent("id", "Approved", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => assertions.HaveOperation((DurableClientOperation)999));
        Assert.Throws<ArgumentNullException>(() => assertions.HaveMatchingOperations(null!));
        Assert.Throws<ArgumentNullException>(() => assertions.HaveOperationSequence(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => assertions.HaveOperationSequence((DurableClientOperation)999));
        assertions.HaveOperationSequence().BeEmpty();
    }
}
