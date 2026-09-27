namespace XBullet.EasyTesting.Observability;

/// <summary>One metric measurement captured from an integration-test process.</summary>
public sealed class TestMetricMeasurement
{
    /// <summary>Creates an immutable captured metric measurement.</summary>
    /// <param name="timestamp">The absolute capture instant supplied by the collector's clock.</param>
    /// <param name="meterName">The publishing meter name.</param>
    /// <param name="instrumentName">The instrument name.</param>
    /// <param name="instrumentType">The runtime instrument type name.</param>
    /// <param name="unit">
    /// The instrument-defined unit text, or <see langword="null"/> when no unit was declared. No unit
    /// conversion is performed.
    /// </param>
    /// <param name="description">The instrument description, or <see langword="null"/> when absent.</param>
    /// <param name="value">The non-null boxed measurement value, retained without numeric conversion.</param>
    /// <param name="tags">
    /// The measurement tags. The dictionary is retained without copying and can contain sensitive or
    /// mutable values; callers should not mutate it after construction.
    /// </param>
    public TestMetricMeasurement(
        DateTimeOffset timestamp,
        string meterName,
        string instrumentName,
        string instrumentType,
        string? unit,
        string? description,
        object value,
        IReadOnlyDictionary<string, object?> tags)
    {
        Timestamp = timestamp;
        MeterName = meterName;
        InstrumentName = instrumentName;
        InstrumentType = instrumentType;
        Unit = unit;
        Description = description;
        Value = value;
        Tags = tags;
    }

    /// <summary>Gets the time at which the measurement was observed.</summary>
    /// <value>The absolute instant returned by the collector's time provider.</value>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the meter name.</summary>
    /// <value>The exact publishing meter name.</value>
    public string MeterName { get; }

    /// <summary>Gets the instrument name.</summary>
    /// <value>The exact instrument name.</value>
    public string InstrumentName { get; }

    /// <summary>Gets the runtime instrument type name.</summary>
    /// <value>The simple runtime type name, such as a counter or histogram implementation.</value>
    public string InstrumentType { get; }

    /// <summary>Gets the instrument unit.</summary>
    /// <value>
    /// The instrument-defined unit text, or <see langword="null"/> when none was declared. The value
    /// is metadata only; measurements are not converted.
    /// </value>
    public string? Unit { get; }

    /// <summary>Gets the instrument description.</summary>
    /// <value>The description supplied by the instrument, or <see langword="null"/> when absent.</value>
    public string? Description { get; }

    /// <summary>Gets the boxed numeric measurement value.</summary>
    /// <value>
    /// The original boxed numeric value without conversion. Collector-produced values are byte,
    /// short, integer, long, float, double, or decimal measurements.
    /// </value>
    public object Value { get; }

    /// <summary>Gets a stable copy of the measurement tags.</summary>
    /// <value>The retained read-only tags. Values are not deep-cloned or redacted.</value>
    public IReadOnlyDictionary<string, object?> Tags { get; }
}
