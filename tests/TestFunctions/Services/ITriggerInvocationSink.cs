using TestFunctions.Models;

namespace TestFunctions.Services;

public interface ITriggerInvocationSink
{
    Task RecordAsync(TriggerInvocation invocation, CancellationToken cancellationToken = default);
}
