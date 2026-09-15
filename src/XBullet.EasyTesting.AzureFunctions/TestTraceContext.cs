using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Configurable distributed-tracing values for a test invocation.</summary>
public sealed class TestTraceContext : TraceContext
{
    internal TestTraceContext(string traceParent = "", string traceState = "")
    {
        TraceParent = traceParent;
        TraceState = traceState;
    }

    /// <inheritdoc />
    public override string TraceParent { get; }

    /// <inheritdoc />
    public override string TraceState { get; }
}
