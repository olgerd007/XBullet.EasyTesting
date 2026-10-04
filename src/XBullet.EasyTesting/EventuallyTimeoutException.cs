namespace XBullet.EasyTesting;

/// <summary>Reports an eventual assertion or condition that did not succeed before its deadline.</summary>
/// <remarks>The inner exception contains the last retried assertion failure, when present.</remarks>
public sealed class EventuallyTimeoutException : TimeoutException
{
    internal EventuallyTimeoutException(
        int attemptCount,
        TimeSpan elapsed,
        EventuallyOptions options,
        Exception? lastFailure)
        : base(
            $"{options.Description ?? "The eventual assertion or condition"} did not succeed within " +
            $"{options.Timeout}. Attempts: {attemptCount}; elapsed: {elapsed}." +
            (lastFailure is null ? string.Empty : $" Last failure: {lastFailure.Message}"),
            lastFailure)
    {
        AttemptCount = attemptCount;
        Elapsed = elapsed;
        Timeout = options.Timeout;
    }

    /// <summary>Gets the number of attempts started before timing out.</summary>
    /// <value>The number of started attempts.</value>
    public int AttemptCount { get; }

    /// <summary>Gets the elapsed duration measured by the configured time provider.</summary>
    /// <value>The total elapsed duration, including time spent in callbacks.</value>
    public TimeSpan Elapsed { get; }

    /// <summary>Gets the configured timeout.</summary>
    /// <value>The maximum cooperative wait duration.</value>
    public TimeSpan Timeout { get; }
}
