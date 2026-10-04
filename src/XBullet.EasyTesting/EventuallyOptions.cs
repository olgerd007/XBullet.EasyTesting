namespace XBullet.EasyTesting;

/// <summary>Configures sequential polling for eventual assertions and conditions.</summary>
public sealed class EventuallyOptions
{
    /// <summary>Gets the maximum cooperative wait duration.</summary>
    /// <value>A positive duration, defaulting to five seconds.</value>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the delay between completed attempts.</summary>
    /// <value>A positive duration, defaulting to fifty milliseconds.</value>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Gets the clock used for deadlines, elapsed time, and polling delays.</summary>
    /// <value>The time provider, defaulting to the system clock.</value>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets an optional description included in timeout diagnostics.</summary>
    /// <value>The expected eventual behavior, or <see langword="null"/>.</value>
    public string? Description { get; init; }

    /// <summary>Gets an optional filter for assertion failures that should be retried.</summary>
    /// <value>A filter, or <see langword="null"/> to retry every non-cancellation assertion failure.</value>
    /// <remarks>Conditions never retry exceptions. Cancellation exceptions are never passed to this filter.</remarks>
    public Func<Exception, bool>? ShouldRetry { get; init; }

    internal void Validate()
    {
        // CancellationTokenSource and Task.Delay share this finite timer range.
        var maximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        if (Timeout <= TimeSpan.Zero || Timeout > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive and within the timer range.");
        }

        if (PollInterval <= TimeSpan.Zero || PollInterval > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), "Poll interval must be positive and within the timer range.");
        }

        ArgumentNullException.ThrowIfNull(TimeProvider);
    }
}
