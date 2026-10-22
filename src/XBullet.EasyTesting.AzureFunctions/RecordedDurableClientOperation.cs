namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Identifies a client call attempt independently of its handler outcome.</summary>
public enum DurableClientOperation
{
    /// <summary>Schedule an orchestration.</summary>
    Schedule,
    /// <summary>Read instance metadata, through either SDK lookup alias.</summary>
    GetInstance,
    /// <summary>Raise an external event.</summary>
    RaiseEvent,
    /// <summary>Wait for an instance to start.</summary>
    WaitForStart,
    /// <summary>Wait for an instance to complete.</summary>
    WaitForCompletion,
    /// <summary>Suspend an instance.</summary>
    Suspend,
    /// <summary>Resume an instance.</summary>
    Resume,
    /// <summary>Request a pageable query.</summary>
    Query,
    /// <summary>Terminate an instance.</summary>
    Terminate,
    /// <summary>Purge one instance.</summary>
    PurgeInstance,
    /// <summary>Purge instances matching a filter.</summary>
    PurgeAll,
    /// <summary>Restart an instance.</summary>
    Restart,
    /// <summary>Rewind an instance.</summary>
    Rewind,
    /// <summary>Read orchestration history.</summary>
    GetHistory
}

/// <summary>A client call recorded before handler dispatch.</summary>
/// <param name="Sequence">The one-based invocation order since the last reset, not completion order.</param>
/// <param name="Operation">The requested operation.</param>
/// <param name="InstanceId">The target instance ID; null for query and bulk purge.</param>
/// <param name="Arguments">SDK parameter names excluding cancellation, with original argument references.</param>
public sealed record RecordedDurableClientOperation(
    long Sequence,
    DurableClientOperation Operation,
    string? InstanceId,
    IReadOnlyDictionary<string, object?> Arguments)
{
    /// <summary>Reads an argument without conversion.</summary>
    /// <typeparam name="T">The expected assignable type.</typeparam>
    /// <param name="name">The case-sensitive SDK parameter name.</param>
    /// <returns>The original typed argument, or default when the argument is null.</returns>
    public T? GetArgument<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var value = Arguments[name];
        return value is null ? default : value is T typed ? typed
            : throw new InvalidOperationException($"Argument '{name}' of {Operation} is not a {typeof(T).FullName}.");
    }
}
