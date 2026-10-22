using System.Text.Json;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class DurableTaskSequenceTests
{
    [Fact]
    public async Task Sequences_return_original_results_and_failures_then_fail_on_exhaustion()
    {
        var result = new object();
        var failure = new InvalidOperationException("private failure");
        var sequence = new TestDurableTaskSequence<object?>().ThenThrow(failure).ThenReturn(result).ThenReturn(null);
        var token = TestContext.Current.CancellationToken;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => sequence.NextAsync(token)));
        Assert.Same(result, await sequence.NextAsync(token));
        Assert.Null(await sequence.NextAsync(token));
        await Assert.ThrowsAsync<TestDurableTaskSequenceExhaustedException>(() => sequence.NextAsync(token));
        Assert.Equal(3, sequence.ConsumedCount);
        Assert.Throws<InvalidOperationException>(() => sequence.ThenReturn(null));
        await sequence.ResetAsync(token);
        Assert.Equal(0, sequence.ConsumedCount);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => sequence.NextAsync(token)));
    }

    [Fact]
    public async Task Canceled_submissions_and_resets_do_not_consume_or_rewind_steps()
    {
        var sequence = new TestDurableTaskSequence<int>().ThenReturn(1).ThenReturn(2);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sequence.NextAsync(canceled.Token));
        Assert.Equal(0, sequence.ConsumedCount);
        Assert.Equal(1, await sequence.NextAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sequence.ResetAsync(canceled.Token).AsTask());
        Assert.Equal(1, sequence.ConsumedCount);
        Assert.Throws<ArgumentNullException>(() => sequence.ThenThrow(null!));
    }

    [Fact]
    public async Task Concurrent_submissions_reserve_distinct_steps_and_diagnostics_exclude_contents()
    {
        var sequence = new TestDurableTaskSequence<int>();
        foreach (var value in Enumerable.Range(1, 20))
        {
            sequence.ThenReturn(value);
        }

        var token = TestContext.Current.CancellationToken;
        var values = await Task.WhenAll(Enumerable.Range(1, 20).Select(_ => Task.Run(() => sequence.NextAsync(token), token)));
        Assert.Equal(Enumerable.Range(1, 20), values.Order());
        var snapshot = await sequence.CaptureDiagnosticsAsync(token);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.Equal("{\"Configured\":20,\"Consumed\":20,\"Remaining\":0}", json);
        await sequence.ResetAsync(token);
        Assert.Equal(json, JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task Standalone_scenario_resources_rewind_sequence_consumption()
    {
        var sequence = new TestDurableTaskSequence<string>().ThenReturn("private-result");
        await using var host = AzureFunctionTestHost.CreateBuilder().UseScenarioResource("Outcomes", sequence).Build();
        var token = TestContext.Current.CancellationToken;
        for (var scenario = 0; scenario < 2; scenario++)
        {
            await host.RunInTestScenarioScopeAsync(async (_, cancellation) =>
            {
                Assert.Equal("private-result", await sequence.NextAsync(cancellation));
                Assert.DoesNotContain("private-result", JsonSerializer.Serialize(await sequence.CaptureDiagnosticsAsync(cancellation)));
            }, token);
            Assert.Equal(0, sequence.ConsumedCount);
        }
    }
}
