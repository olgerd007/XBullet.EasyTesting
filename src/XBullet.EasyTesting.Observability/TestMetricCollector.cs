using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Observability;

/// <summary>Thread-safe listener that captures metric measurements in memory.</summary>
public sealed class TestMetricCollector : ITestScenarioResource, IDisposable
{
    private readonly object _gate = new();
    private readonly List<TestMetricMeasurement> _measurements = [];
    private readonly HashSet<string> _meterNames;
    private readonly int _maximumMeasurements;
    private readonly TimeProvider _timeProvider;
    private readonly MeterListener _listener = new();

    /// <summary>Creates a bounded collector, optionally restricted to exact meter names.</summary>
    public TestMetricCollector(
        TimeProvider? timeProvider = null,
        IEnumerable<string>? meterNames = null,
        int maximumMeasurements = 1_000)
    {
        if (maximumMeasurements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMeasurements));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _maximumMeasurements = maximumMeasurements;
        _meterNames = new HashSet<string>(meterNames ?? [], StringComparer.Ordinal);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (_meterNames.Count == 0 || _meterNames.Contains(instrument.Meter.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<byte>(Record);
        _listener.SetMeasurementEventCallback<short>(Record);
        _listener.SetMeasurementEventCallback<int>(Record);
        _listener.SetMeasurementEventCallback<long>(Record);
        _listener.SetMeasurementEventCallback<float>(Record);
        _listener.SetMeasurementEventCallback<double>(Record);
        _listener.SetMeasurementEventCallback<decimal>(Record);
        _listener.Start();
    }

    /// <summary>Gets a stable copy of captured measurements in recording order.</summary>
    public IReadOnlyList<TestMetricMeasurement> Measurements
    {
        get
        {
            lock (_gate)
            {
                return _measurements.ToArray();
            }
        }
    }

    /// <summary>Gets the number of currently captured measurements.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _measurements.Count;
            }
        }
    }

    /// <summary>Starts a fluent assertion chain over captured measurements.</summary>
    public TestMetricCollectorAssertions Should() => new(this);

    /// <summary>Requests measurements from enabled observable instruments.</summary>
    public void CollectObservableMeasurements() => _listener.RecordObservableInstruments();

    /// <summary>Removes every captured measurement and returns this collector.</summary>
    public TestMetricCollector Reset()
    {
        lock (_gate)
        {
            _measurements.Clear();
        }

        return this;
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var measurements = Measurements.Select(measurement => new
        {
            measurement.Timestamp,
            measurement.MeterName,
            measurement.InstrumentName,
            measurement.InstrumentType,
            measurement.Unit,
            measurement.Description,
            Value = measurement.Value.ToString(),
            Tags = measurement.Tags.ToDictionary(
                property => property.Key,
                property => property.Value?.ToString(),
                StringComparer.Ordinal)
        }).ToArray();
        return ValueTask.FromResult<object?>(new
        {
            Count = measurements.Length,
            Measurements = measurements
        });
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

    private void Record<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where T : struct
    {
        _ = state;
        var capturedTags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            capturedTags[tag.Key] = tag.Value;
        }

        var capturedMeasurement = new TestMetricMeasurement(
            _timeProvider.GetUtcNow(),
            instrument.Meter.Name,
            instrument.Name,
            instrument.GetType().Name,
            instrument.Unit,
            instrument.Description,
            measurement,
            new ReadOnlyDictionary<string, object?>(capturedTags));
        lock (_gate)
        {
            _measurements.Add(capturedMeasurement);
            if (_measurements.Count > _maximumMeasurements)
            {
                _measurements.RemoveRange(0, _measurements.Count - _maximumMeasurements);
            }
        }
    }
}
