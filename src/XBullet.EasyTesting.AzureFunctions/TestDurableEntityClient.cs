using System.Runtime.CompilerServices;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Records entity client operations without a Durable service.</summary>
/// <remarks>Signals record submissions only. Seeded metadata never changes automatically. Configure handlers
/// before concurrent use and await in-flight calls before reset. Query and cleanup require explicit handlers.</remarks>
public sealed class TestDurableEntityClient : DurableEntityClient, ITestScenarioResource
{
    private readonly EntityRecording _recording = new();
    private readonly object _gate = new();
    private readonly Dictionary<EntityInstanceId, EntityMetadata> _entities = [];

    /// <summary>Creates an entity client.</summary>
    /// <param name="name">The non-empty client name.</param>
    public TestDurableEntityClient(string name = "test-client") : base(name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

    /// <summary>Gets all entity client attempts, including handler failures.</summary>
    /// <value>An ordered snapshot excluding invalid and pre-canceled calls.</value>
    public IReadOnlyList<RecordedEntityOperation> Operations => _recording.Snapshot();

    /// <summary>Gets or sets signal submission behavior.</summary>
    /// <value>An optional callback receiving the recorded attempt and caller token; null accepts without delivery.</value>
    public Func<RecordedEntityOperation, CancellationToken, Task>? SignalHandler { get; set; }

    /// <summary>Gets or sets metadata lookup behavior.</summary>
    /// <value>An optional callback overriding fixtures. The fake projects its response to honor state inclusion.</value>
    public Func<EntityInstanceId, bool, CancellationToken, Task<EntityMetadata?>>? GetEntityHandler { get; set; }

    /// <summary>Gets or sets pageable query behavior.</summary>
    /// <value>A required callback returning SDK metadata pages. Typed queries deserialize included states.</value>
    public Func<EntityQuery?, AsyncPageable<EntityMetadata>>? QueryHandler { get; set; }

    /// <summary>Gets or sets cleanup behavior.</summary>
    /// <value>A required callback receiving the request, completion flag, and caller token.</value>
    public Func<CleanEntityStorageRequest?, bool, CancellationToken, Task<CleanEntityStorageResult>>? CleanHandler { get; set; }

    /// <summary>Seeds an SDK metadata fixture, serializing included state immediately.</summary>
    /// <typeparam name="T">The fixture state type.</typeparam>
    /// <param name="metadata">The non-null fixture to copy.</param>
    /// <param name="dataConverter">The state converter, or the SDK JSON default.</param>
    /// <returns>This client for chaining.</returns>
    public TestDurableEntityClient WithEntity<T>(EntityMetadata<T> metadata, DataConverter? dataConverter = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        EntityRecording.Validate(metadata.Id);
        var converter = dataConverter ?? JsonDataConverter.Default;
        var state = metadata.IncludesState ? new SerializedData(converter.Serialize(metadata.State), converter) : null;
        var copy = new EntityMetadata(metadata.Id, state)
        {
            LastModifiedTime = metadata.LastModifiedTime,
            BacklogQueueSize = metadata.BacklogQueueSize,
            LockedBy = metadata.LockedBy
        };
        return WithEntity(copy);
    }

    /// <summary>Seeds serialized SDK metadata without serializing its wrapper again.</summary>
    /// <param name="metadata">The non-null fixture. Scalar values are copied; serialized state and converter are retained.</param>
    /// <returns>This client for chaining.</returns>
    public TestDurableEntityClient WithEntity(EntityMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        EntityRecording.Validate(metadata.Id);
        var copy = Copy(metadata, true);
        lock (_gate)
        {
            _entities[metadata.Id] = copy;
        }

        return this;
    }

    /// <inheritdoc />
    public override Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null,
        SignalEntityOptions? options = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        EntityRecording.Validate(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var call = _recording.Record(EntityOperationKind.Signal, id, operationName, input, options);
        return SignalHandler?.Invoke(call, cancellation) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task<EntityMetadata?> GetEntityAsync(EntityInstanceId id, bool includeState = true,
        CancellationToken cancellation = default)
    {
        var metadata = await ReadAsync(id, includeState, typeof(SerializedData), cancellation).ConfigureAwait(false);
        return metadata is null ? null : Copy(metadata, includeState);
    }

    /// <inheritdoc />
    public override async Task<EntityMetadata<T>?> GetEntityAsync<T>(EntityInstanceId id, bool includeState = true,
        CancellationToken cancellation = default)
    {
        var metadata = await ReadAsync(id, includeState, typeof(T), cancellation).ConfigureAwait(false);
        return metadata is null ? null : Project<T>(metadata, includeState);
    }

    private Task<EntityMetadata?> ReadAsync(EntityInstanceId id, bool includeState, Type resultType, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        EntityRecording.Validate(id);
        _recording.Record(EntityOperationKind.Read, id, options: includeState, resultType: resultType);
        if (GetEntityHandler is { } handler)
        {
            return handler(id, includeState, cancellation);
        }

        lock (_gate)
        {
            return Task.FromResult(_entities.GetValueOrDefault(id));
        }
    }

    /// <inheritdoc />
    public override AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null)
    {
        _recording.Record(EntityOperationKind.Query, options: filter, resultType: typeof(SerializedData));
        var includeState = filter?.IncludeState ?? true;
        return new ProjectedEntities<EntityMetadata>(RequireQuery()(filter), item => Copy(item, includeState));
    }

    /// <inheritdoc />
    public override AsyncPageable<EntityMetadata<T>> GetAllEntitiesAsync<T>(EntityQuery? filter = null)
    {
        _recording.Record(EntityOperationKind.Query, options: filter, resultType: typeof(T));
        var includeState = filter?.IncludeState ?? true;
        return new ProjectedEntities<EntityMetadata<T>>(RequireQuery()(filter), item => Project<T>(item, includeState));
    }

    /// <inheritdoc />
    public override Task<CleanEntityStorageResult> CleanEntityStorageAsync(CleanEntityStorageRequest? request = null,
        bool continueUntilComplete = true, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        _recording.Record(EntityOperationKind.Clean, options: (request, continueUntilComplete));
        return (CleanHandler ?? throw new InvalidOperationException("Configure TestDurableEntityClient.CleanHandler before cleaning storage."))(
            request, continueUntilComplete, cancellation);
    }

    /// <summary>Clears entity fixtures, recorded calls, and sequence numbers, retaining handlers.</summary>
    /// <param name="cancellationToken">Cancels before state changes.</param>
    /// <returns>A completed task after reset.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entities.Clear();
            _recording.Reset();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Captures operation names, entity IDs, and call order.</summary>
    /// <param name="cancellationToken">Cancels before capture.</param>
    /// <returns>A detached snapshot excluding state, inputs, options, and failures.</returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<object?>(_recording.Diagnostics());
    }

    private Func<EntityQuery?, AsyncPageable<EntityMetadata>> RequireQuery() =>
        QueryHandler ?? throw new InvalidOperationException("Configure TestDurableEntityClient.QueryHandler before querying entities.");

    private static EntityMetadata Copy(EntityMetadata source, bool includeState) => new(source.Id,
        includeState && source.IncludesState ? source.State : null)
    {
        LastModifiedTime = source.LastModifiedTime,
        BacklogQueueSize = source.BacklogQueueSize,
        LockedBy = source.LockedBy
    };

    private static EntityMetadata<T> Project<T>(EntityMetadata source, bool includeState)
    {
        return includeState && source.IncludesState
            ? new EntityMetadata<T>(source.Id, source.State.ReadAs<T>()!)
            {
                LastModifiedTime = source.LastModifiedTime,
                BacklogQueueSize = source.BacklogQueueSize,
                LockedBy = source.LockedBy
            }
            : new EntityMetadata<T>(source.Id)
            {
                LastModifiedTime = source.LastModifiedTime,
                BacklogQueueSize = source.BacklogQueueSize,
                LockedBy = source.LockedBy
            };
    }

    private sealed class ProjectedEntities<T>(AsyncPageable<EntityMetadata> source, Func<EntityMetadata, T> project) : AsyncPageable<T>
        where T : notnull
    {
        public override IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null,
            int? pageSizeHint = null) => EnumerateAsync(continuationToken, pageSizeHint);

        private async IAsyncEnumerable<Page<T>> EnumerateAsync(string? continuationToken,
            int? pageSizeHint, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var page in source.AsPages(continuationToken, pageSizeHint).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return new Page<T>(page.Values.Select(project).ToArray(), page.ContinuationToken);
            }
        }
    }
}
