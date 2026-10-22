namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Fluent assertions over client call attempts, without claiming handler success.</summary>
public sealed class TestDurableTaskClientAssertions
{
    private readonly TestDurableTaskClient _client;

    internal TestDurableTaskClientAssertions(TestDurableTaskClient client) => _client = client;

    /// <summary>Requires an exact total number of recorded client attempts.</summary>
    /// <param name="expected">The non-negative operation count.</param>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions HaveCount(int expected)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        var calls = _client.Operations;
        if (calls.Count != expected)
        {
            throw Failure($"Expected {expected} Durable client operation(s), but recorded {calls.Count}.", calls);
        }

        return this;
    }

    /// <summary>Requires that no operations were recorded.</summary>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions BeEmpty() => HaveCount(0);

    /// <summary>Requires an exact count of scheduling attempts for a name and optional ID.</summary>
    /// <param name="orchestratorName">The non-empty, case-sensitive orchestration name.</param>
    /// <param name="instanceId">A case-sensitive instance ID, or null to match any ID.</param>
    /// <param name="times">The non-negative expected count, defaulting to one.</param>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions HaveScheduledOrchestration(string orchestratorName, string? instanceId = null, int times = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorName);
        ValidateInstanceFilter(instanceId);
        return HaveMatches(call => call.Operation == DurableClientOperation.Schedule
            && TestDurableTaskClient.Target(call) == orchestratorName
            && (instanceId is null || call.InstanceId == instanceId), times,
            $"scheduling attempt(s) for '{orchestratorName}' on instance '{instanceId ?? "*"}'");
    }

    /// <summary>Requires an exact count of event attempts.</summary>
    /// <param name="instanceId">The non-empty, case-sensitive target instance ID.</param>
    /// <param name="eventName">The non-empty event name, matched case-insensitively as in the SDK.</param>
    /// <param name="times">The non-negative expected count, defaulting to one.</param>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions HaveRaisedEvent(string instanceId, string eventName, int times = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return HaveMatches(call => call.Operation == DurableClientOperation.RaiseEvent
            && call.InstanceId == instanceId
            && string.Equals(TestDurableTaskClient.Target(call), eventName, StringComparison.OrdinalIgnoreCase),
            times, $"event attempt(s) named '{eventName}' on instance '{instanceId}'");
    }

    /// <summary>Requires an exact count of an operation, optionally scoped to one instance.</summary>
    /// <param name="operation">A defined operation kind.</param>
    /// <param name="instanceId">A case-sensitive instance ID, or null to match any target.</param>
    /// <param name="times">The non-negative expected count, defaulting to one.</param>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions HaveOperation(DurableClientOperation operation, string? instanceId = null, int times = 1)
    {
        ValidateOperation(operation);
        ValidateInstanceFilter(instanceId);
        return HaveMatches(call => call.Operation == operation && (instanceId is null || call.InstanceId == instanceId),
            times, $"{operation} attempt(s) on instance '{instanceId ?? "*"}'");
    }

    /// <summary>Requires an exact count of operations matching a custom argument predicate.</summary>
    /// <param name="predicate">The non-null predicate, run once per recorded operation. Predicate exceptions propagate.</param>
    /// <param name="times">The non-negative expected count, defaulting to one.</param>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions HaveMatchingOperations(Func<RecordedDurableClientOperation, bool> predicate, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return HaveMatches(predicate, times, "operation(s) matching the supplied predicate");
    }

    /// <summary>Requires the complete recorded operation sequence to match exactly.</summary>
    /// <param name="operations">The non-null ordered operation kinds; an empty array requires no operations.</param>
    /// <returns>This assertion object for chaining.</returns>
    public TestDurableTaskClientAssertions HaveOperationSequence(params DurableClientOperation[] operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        foreach (var operation in operations)
        {
            ValidateOperation(operation);
        }

        var calls = _client.Operations;
        if (!calls.Select(call => call.Operation).SequenceEqual(operations))
        {
            throw Failure($"Expected Durable client sequence [{string.Join(", ", operations)}].", calls);
        }

        return this;
    }

    private TestDurableTaskClientAssertions HaveMatches(Func<RecordedDurableClientOperation, bool> predicate, int expected, string description)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        var calls = _client.Operations;
        var actual = calls.Count(predicate);
        if (actual != expected)
        {
            throw Failure($"Expected {expected} {description}, but recorded {actual}.", calls);
        }

        return this;
    }

    private static TestDurableTaskClientVerificationException Failure(string message, IReadOnlyList<RecordedDurableClientOperation> calls)
    {
        var timeline = calls.Count == 0 ? "<none>" : string.Join(Environment.NewLine, calls.Select(call =>
            $"  {call.Sequence}. {call.Operation} instance='{call.InstanceId ?? "*"}' target='{TestDurableTaskClient.Target(call) ?? "-"}'"));
        return new($"{message}{Environment.NewLine}Recorded attempts:{Environment.NewLine}{timeline}");
    }

    private static void ValidateOperation(DurableClientOperation operation)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void ValidateInstanceFilter(string? instanceId)
    {
        if (instanceId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        }
    }
}

/// <summary>Thrown when recorded client attempts do not match an assertion.</summary>
/// <param name="message">The assertion failure and timeline. Names and IDs are included without automatic redaction.</param>
public sealed class TestDurableTaskClientVerificationException(string message) : Exception(message);
