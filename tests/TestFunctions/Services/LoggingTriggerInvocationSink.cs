using Microsoft.Extensions.Logging;
using TestFunctions.Models;

namespace TestFunctions.Services;

public sealed class LoggingTriggerInvocationSink(
    ILogger<LoggingTriggerInvocationSink> logger) : ITriggerInvocationSink
{
    public Task RecordAsync(
        TriggerInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Handled {Trigger} invocation for {Subject}: quantity {Quantity}, detail {Detail}, amount {Amount}",
            invocation.Trigger,
            invocation.Subject,
            invocation.Quantity,
            invocation.Detail,
            invocation.Amount);
        return Task.CompletedTask;
    }
}
