namespace XBullet.EasyTesting.Observability;

/// <summary>Fluently verifies metric measurements captured from a test process.</summary>
public sealed class TestMetricCollectorAssertions
{
    private readonly TestMetricCollector _collector;

    internal TestMetricCollectorAssertions(TestMetricCollector collector)
    {
        _collector = collector;
    }

    /// <summary>Requires exactly the supplied number of captured measurements.</summary>
    /// <param name="expected">The non-negative measurement count required at assertion time.</param>
    /// <returns>This assertion object, for chaining, when the exact count matches.</returns>
    public TestMetricCollectorAssertions HaveCount(int expected)
    {
        if (expected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expected));
        }

        if (_collector.Count != expected)
        {
            throw Failure(
                $"Expected {expected} metric measurement or measurements, " +
                $"but found {_collector.Count}.");
        }

        return this;
    }

    /// <summary>Requires a measurement with the supplied instrument name and value.</summary>
    /// <typeparam name="T">The numeric value type to compare without conversion.</typeparam>
    /// <param name="instrumentName">The non-empty instrument name to match exactly and case-sensitively.</param>
    /// <param name="expectedValue">
    /// The exact typed value required. Numeric values of different runtime types do not compare equal.
    /// </param>
    /// <param name="meterName">
    /// The exact, case-sensitive meter name to require, or <see langword="null"/> to accept any meter.
    /// </param>
    /// <returns>This assertion object, for chaining, when at least one measurement matches.</returns>
    public TestMetricCollectorAssertions ContainMeasurement<T>(
        string instrumentName,
        T expectedValue,
        string? meterName = null)
        where T : struct
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentName);
        var matches = _collector.Measurements.Any(measurement =>
            string.Equals(
                measurement.InstrumentName,
                instrumentName,
                StringComparison.Ordinal) &&
            Equals(measurement.Value, expectedValue) &&
            (meterName is null || string.Equals(
                measurement.MeterName,
                meterName,
                StringComparison.Ordinal)));
        if (!matches)
        {
            throw Failure(
                $"Expected a measurement named '{instrumentName}' with value " +
                $"'{expectedValue}', but none matched." +
                FormatMeasurements(_collector.Measurements));
        }

        return this;
    }

    /// <summary>Requires no measurement with the supplied instrument name.</summary>
    /// <param name="instrumentName">The non-empty instrument name to match exactly and case-sensitively.</param>
    /// <param name="meterName">
    /// The exact, case-sensitive meter name to match, or <see langword="null"/> to match any meter.
    /// </param>
    /// <returns>This assertion object, for chaining, when no measurement matches.</returns>
    public TestMetricCollectorAssertions NotContainMeasurement(
        string instrumentName,
        string? meterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentName);
        var matches = _collector.Measurements.Any(measurement =>
            string.Equals(
                measurement.InstrumentName,
                instrumentName,
                StringComparison.Ordinal) &&
            (meterName is null || string.Equals(
                measurement.MeterName,
                meterName,
                StringComparison.Ordinal)));
        if (matches)
        {
            throw Failure(
                $"Expected no measurement named '{instrumentName}', but one matched." +
                FormatMeasurements(_collector.Measurements));
        }

        return this;
    }

    private static string FormatMeasurements(IReadOnlyList<TestMetricMeasurement> measurements) =>
        measurements.Count == 0
            ? $"{Environment.NewLine}No metric measurements were captured."
            : $"{Environment.NewLine}Captured metric measurements:{Environment.NewLine}" +
                string.Join(
                    Environment.NewLine,
                    measurements.Select(measurement =>
                        $"- {measurement.MeterName}/{measurement.InstrumentName}: " +
                        measurement.Value));

    private static TestMetricVerificationException Failure(string message) => new(message);
}

/// <summary>Thrown when captured metrics do not satisfy a fluent assertion.</summary>
/// <param name="message">
/// The non-null failure description, which can include unredacted instrument names and values.
/// </param>
public sealed class TestMetricVerificationException(string message) : Exception(message);
