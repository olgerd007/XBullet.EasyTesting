using System.Diagnostics;

namespace XBullet.EasyTesting.Observability;

/// <summary>Fluently verifies completed activities captured from a test process.</summary>
public sealed class TestActivityCollectorAssertions
{
    private readonly TestActivityCollector _collector;

    internal TestActivityCollectorAssertions(TestActivityCollector collector)
    {
        _collector = collector;
    }

    /// <summary>Requires exactly the supplied number of captured activities.</summary>
    /// <param name="expected">The non-negative activity count required at assertion time.</param>
    /// <returns>This assertion object, for chaining, when the exact count matches.</returns>
    public TestActivityCollectorAssertions HaveCount(int expected)
    {
        if (expected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expected));
        }

        if (_collector.Count != expected)
        {
            throw Failure($"Expected {expected} activity or activities, but found {_collector.Count}.");
        }

        return this;
    }

    /// <summary>Requires a completed activity matching the supplied operation and optional filters.</summary>
    /// <param name="operationName">The non-empty operation name to match exactly and case-sensitively.</param>
    /// <param name="status">
    /// The status code to require, or <see langword="null"/> to accept any status.
    /// </param>
    /// <param name="sourceName">
    /// The exact, case-sensitive source name to require, or <see langword="null"/> to accept any source.
    /// </param>
    /// <returns>This assertion object, for chaining, when at least one completed activity matches.</returns>
    public TestActivityCollectorAssertions ContainActivity(
        string operationName,
        ActivityStatusCode? status = null,
        string? sourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var matches = _collector.Entries.Any(entry =>
            string.Equals(entry.OperationName, operationName, StringComparison.Ordinal) &&
            (status is null || entry.Status == status) &&
            (sourceName is null || string.Equals(
                entry.SourceName,
                sourceName,
                StringComparison.Ordinal)));
        if (!matches)
        {
            throw Failure(
                $"Expected a completed activity named '{operationName}', but none matched." +
                FormatEntries(_collector.Entries));
        }

        return this;
    }

    /// <summary>Requires no completed activity matching the supplied operation and optional source.</summary>
    /// <param name="operationName">The non-empty operation name to match exactly and case-sensitively.</param>
    /// <param name="sourceName">
    /// The exact, case-sensitive source name to match, or <see langword="null"/> to match every source.
    /// </param>
    /// <returns>This assertion object, for chaining, when no completed activity matches.</returns>
    public TestActivityCollectorAssertions NotContainActivity(
        string operationName,
        string? sourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var matches = _collector.Entries.Any(entry =>
            string.Equals(entry.OperationName, operationName, StringComparison.Ordinal) &&
            (sourceName is null || string.Equals(
                entry.SourceName,
                sourceName,
                StringComparison.Ordinal)));
        if (matches)
        {
            throw Failure(
                $"Expected no completed activity named '{operationName}', but one matched." +
                FormatEntries(_collector.Entries));
        }

        return this;
    }

    private static string FormatEntries(IReadOnlyList<TestActivityEntry> entries) =>
        entries.Count == 0
            ? $"{Environment.NewLine}No activities were captured."
            : $"{Environment.NewLine}Captured activities:{Environment.NewLine}" + string.Join(
                Environment.NewLine,
                entries.Select(entry =>
                    $"- {entry.SourceName}: {entry.OperationName} ({entry.Status})"));

    private static TestActivityVerificationException Failure(string message) => new(message);
}

/// <summary>Thrown when captured activities do not satisfy a fluent assertion.</summary>
/// <param name="message">The non-null failure description, which can include captured activity names and status values.</param>
public sealed class TestActivityVerificationException(string message) : Exception(message);
