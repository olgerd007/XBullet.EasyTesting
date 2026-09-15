namespace XBullet.EasyTesting.Observability;

/// <summary>One metric measurement captured from an integration-test process.</summary>
public sealed class TestMetricMeasurement
{
    /// <summary>Creates an immutable captured metric measurement.</summary>
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
    public DateTimeOffset Timestamp { get; }

    /// <summary>Gets the meter name.</summary>
    public string MeterName { get; }

    /// <summary>Gets the instrument name.</summary>
    public string InstrumentName { get; }

    /// <summary>Gets the runtime instrument type name.</summary>
    public string InstrumentType { get; }

    /// <summary>Gets the instrument unit.</summary>
    public string? Unit { get; }

    /// <summary>Gets the instrument description.</summary>
    public string? Description { get; }

    /// <summary>Gets the boxed numeric measurement value.</summary>
    public object Value { get; }

    /// <summary>Gets a stable copy of the measurement tags.</summary>
    public IReadOnlyDictionary<string, object?> Tags { get; }
}
