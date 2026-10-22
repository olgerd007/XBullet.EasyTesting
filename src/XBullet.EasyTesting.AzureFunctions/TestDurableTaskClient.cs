using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Records orchestration starts for isolated-worker client and HTTP starter tests.</summary>
/// <remarks>
/// No orchestrations run automatically. Scheduling returns an explicit instance ID or a unique
/// generated ID. Management operations require explicit handlers and never change seeded state.
/// Configure handlers before concurrent use. Completion-time ID listing retains the SDK's unsupported default.
/// </remarks>
public sealed partial class TestDurableTaskClient : DurableTaskClient, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<RecordedOrchestrationStart> _starts = [];
    private long _sequence;

    /// <summary>Creates a client without runtime or network dependencies.</summary>
    /// <param name="name">The non-empty client name. Defaults to <c>test-client</c>.</param>
    public TestDurableTaskClient(string name = "test-client")
        : base(name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Entities = new TestDurableEntityClient(name);
    }

    /// <summary>Gets the entity client boundary owned by this test client.</summary>
    /// <value>A recording client reset alongside orchestration fixtures; entities do not execute automatically.</value>
    public override TestDurableEntityClient Entities { get; }

    /// <summary>Gets recorded scheduling attempts in invocation order.</summary>
    /// <value>A snapshot including calls whose handlers failed; pre-canceled calls are excluded.</value>
    public IReadOnlyList<RecordedOrchestrationStart> ScheduledOrchestrations
    {
        get
        {
            lock (_gate)
            {
                return _starts.ToArray();
            }
        }
    }

    /// <summary>Gets or sets scheduling behavior after recording a call.</summary>
    /// <value>An optional callback receiving the call and caller token. Null returns the recorded instance ID.</value>
    public Func<RecordedOrchestrationStart, CancellationToken, Task<string>>? ScheduleHandler { get; set; }

    /// <summary>Gets or sets metadata lookup behavior.</summary>
    /// <value>An optional callback receiving the instance ID, payload inclusion flag, and caller token. Null reads seeded metadata, returning null when absent.</value>
    public Func<string, bool, CancellationToken, Task<OrchestrationMetadata?>>? GetInstanceHandler { get; set; }

    /// <summary>Gets or sets event submission behavior.</summary>
    /// <value>A callback receiving instance ID, event name, original payload, and caller token; required for event submission.</value>
    public Func<string, string, object?, CancellationToken, Task>? RaiseEventHandler { get; set; }

    /// <summary>Gets or sets start-wait behavior.</summary>
    /// <value>A callback receiving instance ID, payload inclusion flag, and caller token; required for start waits.</value>
    public Func<string, bool, CancellationToken, Task<OrchestrationMetadata>>? WaitForStartHandler { get; set; }

    /// <summary>Gets or sets completion-wait behavior.</summary>
    /// <value>A callback receiving instance ID, payload inclusion flag, and caller token; required for completion waits.</value>
    public Func<string, bool, CancellationToken, Task<OrchestrationMetadata>>? WaitForCompletionHandler { get; set; }

    /// <summary>Gets or sets suspension behavior.</summary>
    /// <value>A callback receiving instance ID, optional reason, and caller token; required for suspension.</value>
    public Func<string, string?, CancellationToken, Task>? SuspendHandler { get; set; }

    /// <summary>Gets or sets resumption behavior.</summary>
    /// <value>A callback receiving instance ID, optional reason, and caller token; required for resumption.</value>
    public Func<string, string?, CancellationToken, Task>? ResumeHandler { get; set; }

    /// <summary>Gets or sets query behavior.</summary>
    /// <value>A callback receiving the original query; required for pageable queries. Cancellation is handled by the returned pageable.</value>
    public Func<OrchestrationQuery?, AsyncPageable<OrchestrationMetadata>>? QueryHandler { get; set; }

    /// <inheritdoc />
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorName.Name);
        if (options?.InstanceId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.InstanceId);
        }

        RecordedOrchestrationStart call;
        lock (_gate)
        {
            var instanceId = options?.InstanceId ?? $"test-instance-{++_sequence}";
            call = new(orchestratorName, input, options, instanceId);
            _starts.Add(call);
            Record(DurableClientOperation.Schedule, instanceId,
                ("orchestratorName", orchestratorName), ("input", input), ("options", options));
        }

        var handler = ScheduleHandler;
        var result = handler is null
            ? call.InstanceId
            : await handler(call, cancellation).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        return result;
    }

    /// <inheritdoc />
    public override Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId,
        bool getInputsAndOutputs = false,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Record(DurableClientOperation.GetInstance, instanceId, ("getInputsAndOutputs", getInputsAndOutputs));
        var handler = GetInstanceHandler;
        if (handler is not null)
        {
            return handler(instanceId, getInputsAndOutputs, cancellation);
        }

        lock (_gate)
        {
            return Task.FromResult(_instances.TryGetValue(instanceId, out var metadata)
                ? TestOrchestrationMetadataBuilder.Copy(metadata, getInputsAndOutputs)
                : null);
        }
    }

    /// <inheritdoc />
    public override Task RaiseEventAsync(
        string instanceId,
        string eventName,
        object? eventPayload = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        Record(DurableClientOperation.RaiseEvent, instanceId, ("eventName", eventName), ("eventPayload", eventPayload));
        return RequireHandler(RaiseEventHandler, nameof(RaiseEventHandler))(
            instanceId, eventName, eventPayload, cancellation);
    }

    /// <inheritdoc />
    public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Record(DurableClientOperation.WaitForStart, instanceId, ("getInputsAndOutputs", getInputsAndOutputs));
        return RequireHandler(WaitForStartHandler, nameof(WaitForStartHandler))(
            instanceId, getInputsAndOutputs, cancellation);
    }

    /// <inheritdoc />
    public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Record(DurableClientOperation.WaitForCompletion, instanceId, ("getInputsAndOutputs", getInputsAndOutputs));
        return RequireHandler(WaitForCompletionHandler, nameof(WaitForCompletionHandler))(
            instanceId, getInputsAndOutputs, cancellation);
    }

    /// <inheritdoc />
    public override Task SuspendInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Record(DurableClientOperation.Suspend, instanceId, ("reason", reason));
        return RequireHandler(SuspendHandler, nameof(SuspendHandler))(instanceId, reason, cancellation);
    }

    /// <inheritdoc />
    public override Task ResumeInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Record(DurableClientOperation.Resume, instanceId, ("reason", reason));
        return RequireHandler(ResumeHandler, nameof(ResumeHandler))(instanceId, reason, cancellation);
    }

    /// <inheritdoc />
    public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
    {
        Record(DurableClientOperation.Query, null, ("filter", filter));
        return RequireHandler(QueryHandler, nameof(QueryHandler))(filter);
    }

    /// <summary>Completes immediately; this client owns no external resources.</summary>
    /// <returns>A completed value task. Recorded calls remain available.</returns>
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static T RequireHandler<T>(T? handler, string name)
        where T : Delegate =>
        handler ?? throw new InvalidOperationException($"Configure {nameof(TestDurableTaskClient)}.{name} before calling this operation. No Durable runtime is running.");
}
