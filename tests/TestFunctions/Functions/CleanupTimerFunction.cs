using Microsoft.Azure.Functions.Worker;
using TestFunctions.Models;
using TestFunctions.Services;

namespace TestFunctions.Functions;

public sealed class CleanupTimerFunction(ITriggerInvocationSink sink)
{
    [Function(nameof(CleanupTimerFunction))]
    public Task RunAsync(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        FunctionContext context) =>
        sink.RecordAsync(
            new TriggerInvocation("timer", "cleanup", 0, timer.IsPastDue),
            context.CancellationToken);
}
