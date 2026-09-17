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
public sealed class TestMetricVerificationException(string message) : Exception(message);
