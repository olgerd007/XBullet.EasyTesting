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
public sealed class TestActivityVerificationException(string message) : Exception(message);
