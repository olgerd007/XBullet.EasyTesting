using DurableTask.Core.History;
using Microsoft.DurableTask.Client;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableTaskClient
{
    /// <summary>Gets or sets termination behavior.</summary>
    /// <value>A required callback receiving instance ID, original options, and caller token; no state changes occur automatically.</value>
    public Func<string, TerminateInstanceOptions?, CancellationToken, Task>? TerminateHandler { get; set; }

    /// <summary>Gets or sets single-instance purge behavior.</summary>
    /// <value>A required callback receiving instance ID, original options, and caller token.</value>
    public Func<string, PurgeInstanceOptions?, CancellationToken, Task<PurgeResult>>? PurgeHandler { get; set; }

    /// <summary>Gets or sets bulk purge behavior.</summary>
    /// <value>A required callback receiving the original filter, options, and caller token.</value>
    public Func<PurgeInstancesFilter, PurgeInstanceOptions?, CancellationToken, Task<PurgeResult>>? PurgeAllHandler { get; set; }

    /// <summary>Gets or sets restart behavior.</summary>
    /// <value>A required callback receiving instance ID, the new-ID flag, and caller token. Its result must be non-empty.</value>
    public Func<string, bool, CancellationToken, Task<string>>? RestartHandler { get; set; }

    /// <summary>Gets or sets rewind behavior.</summary>
    /// <value>A required callback receiving instance ID, reason, and caller token.</value>
    public Func<string, string, CancellationToken, Task>? RewindHandler { get; set; }

    /// <summary>Gets or sets history retrieval behavior.</summary>
    /// <value>A required callback receiving instance ID and caller token. History is not generated or replayed.</value>
    public Func<string, CancellationToken, Task<IList<HistoryEvent>>>? HistoryHandler { get; set; }

    /// <inheritdoc />
    public override Task TerminateInstanceAsync(string instanceId, TerminateInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        ValidateInstance(instanceId, cancellation);
        Record(DurableClientOperation.Terminate, instanceId, ("options", options));
        return RequireHandler(TerminateHandler, nameof(TerminateHandler))(instanceId, options, cancellation);
    }

    /// <inheritdoc />
    public override Task<PurgeResult> PurgeInstanceAsync(string instanceId, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        ValidateInstance(instanceId, cancellation);
        Record(DurableClientOperation.PurgeInstance, instanceId, ("options", options));
        return RequireHandler(PurgeHandler, nameof(PurgeHandler))(instanceId, options, cancellation);
    }

    /// <inheritdoc />
    public override Task<PurgeResult> PurgeAllInstancesAsync(PurgeInstancesFilter filter, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);
        Record(DurableClientOperation.PurgeAll, null, ("filter", filter), ("options", options));
        return RequireHandler(PurgeAllHandler, nameof(PurgeAllHandler))(filter, options, cancellation);
    }

    /// <inheritdoc />
    public override async Task<string> RestartAsync(string instanceId, bool restartWithNewInstanceId = false, CancellationToken cancellation = default)
    {
        ValidateInstance(instanceId, cancellation);
        Record(DurableClientOperation.Restart, instanceId, ("restartWithNewInstanceId", restartWithNewInstanceId));
        var result = await RequireHandler(RestartHandler, nameof(RestartHandler))(instanceId, restartWithNewInstanceId, cancellation).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        return result;
    }

    /// <inheritdoc />
    public override Task RewindInstanceAsync(string instanceId, string reason, CancellationToken cancellation = default)
    {
        ValidateInstance(instanceId, cancellation);
        ArgumentNullException.ThrowIfNull(reason);
        Record(DurableClientOperation.Rewind, instanceId, ("reason", reason));
        return RequireHandler(RewindHandler, nameof(RewindHandler))(instanceId, reason, cancellation);
    }

    /// <inheritdoc />
    public override Task<IList<HistoryEvent>> GetOrchestrationHistoryAsync(string instanceId, CancellationToken cancellation = default)
    {
        ValidateInstance(instanceId, cancellation);
        Record(DurableClientOperation.GetHistory, instanceId);
        return RequireHandler(HistoryHandler, nameof(HistoryHandler))(instanceId, cancellation);
    }

    private static void ValidateInstance(string instanceId, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
    }
}
