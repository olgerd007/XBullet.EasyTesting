using Microsoft.Extensions.Time.Testing;
using XBullet.EasyTesting;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class EventuallyTests
{
    [Fact]
    // Exercise the intentionally token-free convenience overloads.
#pragma warning disable xUnit1051
    public async Task Default_overloads_complete_immediately_when_successful()
    {
        var calls = 0;
        await Eventually.AssertAsync(() => calls++);
        await Eventually.AssertAsync(() => calls++, TestContext.Current.CancellationToken);
        await Eventually.AssertAsync(_ => { calls++; return Task.CompletedTask; });
        await Eventually.AssertAsync(_ => { calls++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        await Eventually.WaitUntilAsync(() => { calls++; return true; });
        await Eventually.WaitUntilAsync(() => { calls++; return true; }, TestContext.Current.CancellationToken);
        await Eventually.WaitUntilAsync(_ => { calls++; return Task.FromResult(true); });
        await Eventually.WaitUntilAsync(_ => { calls++; return Task.FromResult(true); }, TestContext.Current.CancellationToken);
        await Eventually.AssertAsync(() => { calls++; return Task.CompletedTask; });
        await Eventually.AssertAsync(() => { calls++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        await Eventually.WaitUntilAsync(() => { calls++; return Task.FromResult(true); });
        await Eventually.WaitUntilAsync(() => { calls++; return Task.FromResult(true); }, TestContext.Current.CancellationToken);
        Assert.Equal(12, calls);
    }
#pragma warning restore xUnit1051

    [Fact]
    public async Task Assertions_retry_using_the_configured_clock()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var wait = Eventually.AssertAsync(() => Assert.Equal(2, ++attempts), Options(clock), TestContext.Current.CancellationToken);
        Assert.Equal(1, attempts);
        Assert.False(wait.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        await Complete(wait);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conditions_poll_false_results_without_overlapping_attempts(bool asynchronous)
    {
        var clock = new FakeTimeProvider();
        var calls = 0;
        var firstProbe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = asynchronous
            ? Eventually.WaitUntilAsync(_ => ++calls == 1 ? firstProbe.Task : Task.FromResult(true), Options(clock), TestContext.Current.CancellationToken)
            : Eventually.WaitUntilAsync(() => ++calls == 2, Options(clock), TestContext.Current.CancellationToken);

        if (asynchronous)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Equal(1, calls);
            firstProbe.SetResult(true);
            await Complete(wait);
            Assert.Equal(1, calls);
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await Complete(wait);
            Assert.Equal(2, calls);
        }
    }

    [Fact]
    public async Task Parameterless_async_callbacks_are_awaited_and_retried()
    {
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var wait = Eventually.AssertAsync(async () =>
        {
            await Task.CompletedTask;
            Assert.Equal(2, ++attempts);
        }, Options(clock), TestContext.Current.CancellationToken);
        Assert.Equal(1, attempts);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        await Complete(wait);

        attempts = 0;
        wait = Eventually.WaitUntilAsync(() => Task.FromResult(++attempts == 2), Options(clock), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        await Complete(wait);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Timeout_retains_the_latest_assertion_failure_and_attempt_diagnostics()
    {
        var clock = new FakeTimeProvider();
        var first = new InvalidOperationException("Not published yet.");
        var last = new InvalidOperationException("Wrong payload.");
        var calls = 0;
        var wait = Eventually.AssertAsync(() =>
        {
            if (++calls == 1)
            {
                throw first;
            }

            clock.Advance(TimeSpan.FromSeconds(1));
            throw last;
        }, Options(clock), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(() => Complete(wait));
        Assert.Same(last, exception.InnerException);
        Assert.Equal(2, exception.AttemptCount);
        Assert.Equal(TimeSpan.FromSeconds(1.05), exception.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(1), exception.Timeout);
        Assert.Contains("Order becomes visible", exception.Message);
        Assert.Contains("Wrong payload.", exception.Message);
    }

    [Fact]
    public async Task Timeout_limits_the_delay_and_does_not_start_another_attempt()
    {
        var clock = new FakeTimeProvider();
        var calls = 0;
        var wait = Eventually.WaitUntilAsync(() => { calls++; return false; }, new EventuallyOptions
        {
            TimeProvider = clock,
            Timeout = TimeSpan.FromMilliseconds(25),
            PollInterval = TimeSpan.FromSeconds(1)
        }, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(25));

        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(() => Complete(wait));
        Assert.Null(exception.InnerException);
        Assert.Equal(1, calls);
        Assert.Equal(1, exception.AttemptCount);
        Assert.Equal(TimeSpan.FromMilliseconds(25), exception.Elapsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_preserves_the_callers_token(bool duringProbe)
    {
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var wait = duringProbe
            ? Eventually.AssertAsync(token => Task.Delay(Timeout.InfiniteTimeSpan, token), Options(clock), cancellation.Token)
            : Eventually.WaitUntilAsync(() => false, Options(clock), cancellation.Token);
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Complete(wait));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Already_canceled_call_never_invokes_the_callback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Eventually.AssertAsync(() => calls++, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Deadline_cancels_an_inflight_probe_and_waits_for_its_finally_block()
    {
        var clock = new FakeTimeProvider();
        var disposed = false;
        var wait = Eventually.WaitUntilAsync(async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return false;
            }
            finally
            {
                disposed = true;
            }
        }, Options(clock), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(() => Complete(wait));
        Assert.True(disposed);
        Assert.Equal(1, exception.AttemptCount);
    }

    [Fact]
    public async Task Uncooperative_probe_is_awaited_and_late_success_is_rejected()
    {
        var clock = new FakeTimeProvider();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Eventually.WaitUntilAsync(_ => completion.Task, Options(clock), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(wait.IsCompleted);
        completion.SetResult(true);
        await Assert.ThrowsAsync<EventuallyTimeoutException>(() => Complete(wait));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Condition_errors_fail_immediately(bool asynchronous)
    {
        var failure = new InvalidOperationException("Query failed.");
        var wait = asynchronous
            ? Eventually.WaitUntilAsync(_ => Task.FromException<bool>(failure), TestContext.Current.CancellationToken)
            : Eventually.WaitUntilAsync((Func<bool>)(() => throw failure), TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(wait));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task Assertion_filter_can_fail_fast_for_unexpected_errors()
    {
        var failure = new InvalidOperationException("Broken query.");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Eventually.AssertAsync(
            (Action)(() => throw failure),
            new EventuallyOptions { ShouldRetry = error => error is Xunit.Sdk.XunitException },
            TestContext.Current.CancellationToken));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task Cancellation_exceptions_are_never_retried_or_filtered()
    {
        var failure = new OperationCanceledException("Callback canceled independently.");
        var filterCalls = 0;
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => Eventually.AssertAsync(
            (Action)(() => throw failure),
            new EventuallyOptions { ShouldRetry = _ => { filterCalls++; return true; } },
            TestContext.Current.CancellationToken));
        Assert.Same(failure, exception);
        Assert.Equal(0, filterCalls);
    }

    [Fact]
    public async Task Caller_cancellation_wins_when_the_deadline_also_expires()
    {
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var wait = Eventually.WaitUntilAsync(() =>
        {
            cancellation.Cancel();
            clock.Advance(TimeSpan.FromSeconds(1));
            return true;
        }, Options(clock), cancellation.Token);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Complete(wait));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Theory]
    [InlineData(0, 50, "Timeout")]
    [InlineData(-1, 50, "Timeout")]
    [InlineData(4294967295, 50, "Timeout")]
    [InlineData(1000, 0, "PollInterval")]
    [InlineData(1000, -1, "PollInterval")]
    [InlineData(1000, 4294967295, "PollInterval")]
    public async Task Invalid_durations_are_rejected_before_the_callback(long timeout, long interval, string parameter)
    {
        var calls = 0;
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Eventually.WaitUntilAsync(
            () => { calls++; return true; },
            new EventuallyOptions { Timeout = TimeSpan.FromMilliseconds(timeout), PollInterval = TimeSpan.FromMilliseconds(interval) },
            TestContext.Current.CancellationToken));
        Assert.Equal(parameter, exception.ParamName);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Null_callbacks_options_and_clocks_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Eventually.AssertAsync((Action)null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Eventually.AssertAsync((Func<CancellationToken, Task>)null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Eventually.WaitUntilAsync((Func<bool>)null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Eventually.WaitUntilAsync((Func<CancellationToken, Task<bool>>)null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Eventually.WaitUntilAsync(() => true, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Eventually.WaitUntilAsync(
            () => true, new EventuallyOptions { TimeProvider = null! }, TestContext.Current.CancellationToken));
    }

    private static EventuallyOptions Options(TimeProvider clock) => new()
    {
        TimeProvider = clock,
        Timeout = TimeSpan.FromSeconds(1),
        PollInterval = TimeSpan.FromMilliseconds(50),
        Description = "Order becomes visible"
    };

    private static Task Complete(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
}
