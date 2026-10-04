namespace XBullet.EasyTesting;

/// <summary>Waits for assertions or conditions to succeed without overlapping attempts.</summary>
/// <remarks>
/// The first attempt runs immediately. Deadlines cancel the token supplied to asynchronous callbacks.
/// Callbacks must observe cancellation or complete promptly; synchronous or uncooperative callbacks
/// cannot be interrupted. No callback is abandoned when the wait ends.
/// </remarks>
public static class Eventually
{
    /// <summary>Waits for an assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task AssertAsync(Action assertion) =>
        AssertAsync(assertion, new EventuallyOptions(), CancellationToken.None);

    /// <summary>Waits for an assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task AssertAsync(Action assertion, CancellationToken cancellationToken) =>
        AssertAsync(assertion, new EventuallyOptions(), cancellationToken);

    /// <summary>Waits for an assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <param name="options">The timeout, polling, clock, and diagnostic settings.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <exception cref="EventuallyTimeoutException">The deadline expires before success.</exception>
    public static Task AssertAsync(Action assertion, EventuallyOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        return RunAsync(token =>
        {
            assertion();
            return Task.FromResult(true);
        }, options, true, cancellationToken);
    }

    /// <summary>Waits for an asynchronous assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task AssertAsync(Func<CancellationToken, Task> assertion) =>
        AssertAsync(assertion, new EventuallyOptions(), CancellationToken.None);

    /// <summary>Waits for an asynchronous assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task AssertAsync(Func<CancellationToken, Task> assertion, CancellationToken cancellationToken) =>
        AssertAsync(assertion, new EventuallyOptions(), cancellationToken);

    /// <summary>Waits for an asynchronous assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <param name="options">The timeout, polling, clock, and diagnostic settings.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <exception cref="EventuallyTimeoutException">The deadline expires before success.</exception>
    public static Task AssertAsync(Func<CancellationToken, Task> assertion, EventuallyOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        return RunAsync(async token =>
        {
            await assertion(token).ConfigureAwait(false);
            return true;
        }, options, true, cancellationToken);
    }

    /// <summary>Waits for a condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task WaitUntilAsync(Func<bool> condition) =>
        WaitUntilAsync(condition, new EventuallyOptions(), CancellationToken.None);

    /// <summary>Waits for a condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken) =>
        WaitUntilAsync(condition, new EventuallyOptions(), cancellationToken);

    /// <summary>Waits for a condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <param name="options">The timeout, polling, clock, and diagnostic settings.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <exception cref="EventuallyTimeoutException">The deadline expires before success.</exception>
    public static Task WaitUntilAsync(Func<bool> condition, EventuallyOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return RunAsync(_ => Task.FromResult(condition()), options, false, cancellationToken);
    }

    /// <summary>Waits for an asynchronous condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task WaitUntilAsync(Func<CancellationToken, Task<bool>> condition) =>
        WaitUntilAsync(condition, new EventuallyOptions(), CancellationToken.None);

    /// <summary>Waits for an asynchronous condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    public static Task WaitUntilAsync(Func<CancellationToken, Task<bool>> condition, CancellationToken cancellationToken) =>
        WaitUntilAsync(condition, new EventuallyOptions(), cancellationToken);

    /// <summary>Waits for an asynchronous condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <param name="options">The timeout, polling, clock, and diagnostic settings.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <exception cref="EventuallyTimeoutException">The deadline expires before success.</exception>
    public static Task WaitUntilAsync(Func<CancellationToken, Task<bool>> condition, EventuallyOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return RunAsync(condition, options, false, cancellationToken);
    }

    /// <summary>Waits for an asynchronous assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <remarks>The callback must finish promptly because it does not receive the deadline token.</remarks>
    public static Task AssertAsync(Func<Task> assertion) =>
        AssertAsync(assertion, new EventuallyOptions(), CancellationToken.None);

    /// <summary>Waits for an asynchronous assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <remarks>The callback must finish promptly because it does not receive the deadline token.</remarks>
    public static Task AssertAsync(Func<Task> assertion, CancellationToken cancellationToken) =>
        AssertAsync(assertion, new EventuallyOptions(), cancellationToken);

    /// <summary>Waits for an asynchronous assertion to succeed.</summary>
    /// <param name="assertion">The assertion to evaluate on each attempt.</param>
    /// <param name="options">The timeout, polling, clock, and diagnostic settings.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <remarks>The callback must finish promptly because it does not receive the deadline token.</remarks>
    public static Task AssertAsync(Func<Task> assertion, EventuallyOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        return AssertAsync(_ => assertion(), options, cancellationToken);
    }

    /// <summary>Waits for an asynchronous condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <remarks>The callback must finish promptly because it does not receive the deadline token.</remarks>
    public static Task WaitUntilAsync(Func<Task<bool>> condition) =>
        WaitUntilAsync(condition, new EventuallyOptions(), CancellationToken.None);

    /// <summary>Waits for an asynchronous condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <remarks>The callback must finish promptly because it does not receive the deadline token.</remarks>
    public static Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken) =>
        WaitUntilAsync(condition, new EventuallyOptions(), cancellationToken);

    /// <summary>Waits for an asynchronous condition to succeed.</summary>
    /// <param name="condition">The condition to evaluate on each attempt.</param>
    /// <param name="options">The timeout, polling, clock, and diagnostic settings.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes on success, cancellation, or failure.</returns>
    /// <remarks>The callback must finish promptly because it does not receive the deadline token.</remarks>
    public static Task WaitUntilAsync(Func<Task<bool>> condition, EventuallyOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return WaitUntilAsync(_ => condition(), options, cancellationToken);
    }

    private static async Task RunAsync(
        Func<CancellationToken, Task<bool>> probe,
        EventuallyOptions options,
        bool retryFailures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var clock = options.TimeProvider;
        var started = clock.GetTimestamp();
        using var deadline = new CancellationTokenSource(options.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var attempts = 0;
        Exception? lastFailure = null;

        void CheckDeadline()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested || clock.GetElapsedTime(started) >= options.Timeout)
            {
                throw new EventuallyTimeoutException(attempts, clock.GetElapsedTime(started), options, lastFailure);
            }
        }

        while (true)
        {
            CheckDeadline();
            var succeeded = false;
            attempts++;
            try
            {
                succeeded = await probe(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                CheckDeadline();
                throw;
            }
            catch (Exception exception) when (
                retryFailures &&
                exception is not OperationCanceledException &&
                (options.ShouldRetry?.Invoke(exception) ?? true))
            {
                lastFailure = exception;
            }

            CheckDeadline();
            if (succeeded)
            {
                return;
            }

            var remaining = options.Timeout - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                CheckDeadline();
            }

            var delay = remaining < options.PollInterval ? remaining : options.PollInterval;
            try
            {
                await Task.Delay(delay, clock, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                CheckDeadline();
                throw;
            }
        }
    }
}
