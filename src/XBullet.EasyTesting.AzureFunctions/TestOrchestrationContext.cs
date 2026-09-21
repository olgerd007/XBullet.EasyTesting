using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>
/// An in-memory orchestration context that records activity calls and dispatches them to test code.
/// </summary>
/// <remarks>
/// Only activity invocation is emulated. Members that require Durable Task runtime state throw
/// <see cref="NotSupportedException"/>.
/// </remarks>
public sealed class TestOrchestrationContext : TaskOrchestrationContext
{
    private readonly List<RecordedActivityCall> _activityCalls = [];
    private Func<RecordedActivityCall, Task<object?>> _activityHandler;

    /// <summary>Initializes a context that dispatches activity calls through <paramref name="activityHandler"/>.</summary>
    public TestOrchestrationContext(Func<RecordedActivityCall, Task<object?>> activityHandler)
    {
        ArgumentNullException.ThrowIfNull(activityHandler);
        _activityHandler = activityHandler;
    }

    /// <summary>Gets or sets the delegate used to execute scheduled activities.</summary>
    public Func<RecordedActivityCall, Task<object?>> ActivityHandler
    {
        get => _activityHandler;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _activityHandler = value;
        }
    }

    /// <summary>Gets activity calls in scheduling order.</summary>
    public IReadOnlyList<RecordedActivityCall> ActivityCalls => _activityCalls;

    /// <inheritdoc />
    public override TaskName Name => throw Unsupported();

    /// <inheritdoc />
    public override string InstanceId => throw Unsupported();

    /// <inheritdoc />
    public override ParentOrchestrationInstance? Parent => throw Unsupported();

    /// <inheritdoc />
    public override DateTime CurrentUtcDateTime => throw Unsupported();

    /// <inheritdoc />
    public override bool IsReplaying => throw Unsupported();

    /// <inheritdoc />
    public override string Version => throw Unsupported();

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> Properties => throw Unsupported();

    /// <inheritdoc />
    public override TaskOrchestrationEntityFeature Entities => throw Unsupported();

    /// <inheritdoc />
    public override ILoggerFactory ReplaySafeLoggerFactory => throw Unsupported();

    /// <inheritdoc />
    protected override ILoggerFactory LoggerFactory => throw Unsupported();

    /// <inheritdoc />
    public override T GetInput<T>() => throw Unsupported();

    /// <inheritdoc />
    public override async Task<TResult> CallActivityAsync<TResult>(
        TaskName name,
        object? input = null,
        TaskOptions? options = null)
    {
        var call = new RecordedActivityCall(name.Name, input, options, typeof(TResult));
        _activityCalls.Add(call);

        var result = await ActivityHandler(call).ConfigureAwait(false);
        if (result is TResult typedResult)
        {
            return typedResult;
        }

        if (result is null && default(TResult) is null)
        {
            return default!;
        }

        throw new InvalidOperationException(
            $"Activity '{name}' returned {result?.GetType().FullName ?? "null"}, " +
            $"which cannot be assigned to {typeof(TResult).FullName}.");
    }

    /// <inheritdoc />
    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken) =>
        throw Unsupported();

    /// <inheritdoc />
    public override Task<T> WaitForExternalEvent<T>(
        string eventName,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    /// <inheritdoc />
    public override void SendEvent(string instanceId, string eventName, object payload) =>
        throw Unsupported();

    /// <inheritdoc />
    public override void SetCustomStatus(object? customStatus) => throw Unsupported();

    /// <inheritdoc />
    public override Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName,
        object? input = null,
        TaskOptions? options = null) =>
        throw Unsupported();

    /// <inheritdoc />
    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true) =>
        throw Unsupported();

    /// <inheritdoc />
    public override void ContinueAsNew(ContinueAsNewOptions options) => throw Unsupported();

    /// <inheritdoc />
    public override Guid NewGuid() => throw Unsupported();

    /// <inheritdoc />
    public override int CompareVersionTo(string version) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("This Durable Task orchestration operation is not supported by the in-memory test context.");
}
