using TestFunctions.Models;
using TestFunctions.Services;

namespace TestFunctions.IntegrationTests;

internal sealed class RecordingTriggerInvocationSink : ITriggerInvocationSink
{
    private readonly List<TriggerInvocation> _invocations = [];

    public IReadOnlyList<TriggerInvocation> Invocations => _invocations;

    public Task RecordAsync(
        TriggerInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        _invocations.Add(invocation);
        return Task.CompletedTask;
    }
}
