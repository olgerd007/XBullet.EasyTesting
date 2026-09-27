using Microsoft.Extensions.Logging;

namespace XBullet.EasyTesting.Observability;

/// <summary>Fluently verifies structured logs captured from a test host.</summary>
public sealed class TestLogCollectorAssertions
{
    private readonly TestLogCollector _collector;

    internal TestLogCollectorAssertions(TestLogCollector collector)
    {
        _collector = collector;
    }

    /// <summary>Requires exactly the supplied number of captured log entries.</summary>
    /// <param name="expected">The non-negative log-entry count required at assertion time.</param>
    /// <returns>This assertion object, for chaining, when the exact count matches.</returns>
    public TestLogCollectorAssertions HaveCount(int expected)
    {
        if (expected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expected));
        }

        if (_collector.Count != expected)
        {
            throw Failure($"Expected {expected} log entry or entries, but found {_collector.Count}.");
        }

        return this;
    }

    /// <summary>Requires a captured log whose message contains the supplied text.</summary>
    /// <param name="expectedText">The non-empty text that the formatted message must contain using ordinal matching.</param>
    /// <param name="level">The exact log level to require, or <see langword="null"/> to accept any level.</param>
    /// <param name="category">
    /// The exact, case-sensitive category to require, or <see langword="null"/> to accept any category.
    /// </param>
    /// <returns>This assertion object, for chaining, when at least one entry matches.</returns>
    public TestLogCollectorAssertions ContainMessage(
        string expectedText,
        LogLevel? level = null,
        string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedText);
        var matches = _collector.Entries.Any(entry =>
            entry.Message.Contains(expectedText, StringComparison.Ordinal) &&
            (level is null || entry.Level == level) &&
            (category is null || string.Equals(
                entry.Category,
                category,
                StringComparison.Ordinal)));
        if (!matches)
        {
            throw Failure(
                $"Expected a captured log containing '{expectedText}', but none matched." +
                FormatEntries(_collector.Entries));
        }

        return this;
    }

    /// <summary>Requires no captured log whose message contains the supplied text.</summary>
    /// <param name="unexpectedText">The non-empty text matched ordinally against formatted messages.</param>
    /// <param name="level">The exact log level to match, or <see langword="null"/> to match any level.</param>
    /// <param name="category">
    /// The exact, case-sensitive category to match, or <see langword="null"/> to match any category.
    /// </param>
    /// <returns>This assertion object, for chaining, when no entry matches.</returns>
    public TestLogCollectorAssertions NotContainMessage(
        string unexpectedText,
        LogLevel? level = null,
        string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unexpectedText);
        var matches = _collector.Entries.Any(entry =>
            entry.Message.Contains(unexpectedText, StringComparison.Ordinal) &&
            (level is null || entry.Level == level) &&
            (category is null || string.Equals(
                entry.Category,
                category,
                StringComparison.Ordinal)));
        if (matches)
        {
            throw Failure(
                $"Expected no captured log containing '{unexpectedText}', but one matched." +
                FormatEntries(_collector.Entries));
        }

        return this;
    }

    private static string FormatEntries(IReadOnlyList<TestLogEntry> entries) =>
        entries.Count == 0
            ? $"{Environment.NewLine}No logs were captured."
            : $"{Environment.NewLine}Captured logs:{Environment.NewLine}" + string.Join(
                Environment.NewLine,
                entries.Select(entry => $"- {entry.Level} {entry.Category}: {entry.Message}"));

    private static TestLogVerificationException Failure(string message) => new(message);
}

/// <summary>Thrown when captured logs do not satisfy a fluent assertion.</summary>
/// <param name="message">
/// The non-null failure description, which can include unredacted captured categories and messages.
/// </param>
public sealed class TestLogVerificationException(string message) : Exception(message);
