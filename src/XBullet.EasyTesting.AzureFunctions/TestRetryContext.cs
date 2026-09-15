using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Retry values for a test invocation.</summary>
public sealed class TestRetryContext : RetryContext
{
    /// <summary>Creates retry state for an invocation.</summary>
    public TestRetryContext(int retryCount = 0, int maxRetryCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetryCount);
        if (retryCount > maxRetryCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryCount),
                retryCount,
                "Retry count cannot exceed the maximum retry count.");
        }

        RetryCount = retryCount;
        MaxRetryCount = maxRetryCount;
    }

    /// <inheritdoc />
    public override int RetryCount { get; }

    /// <inheritdoc />
    public override int MaxRetryCount { get; }
}
