using System.Collections.ObjectModel;
using System.Diagnostics;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Observability;

/// <summary>Thread-safe listener that captures completed activities in memory.</summary>
public sealed class TestActivityCollector : ITestScenarioResource, IDisposable
{
    private readonly object _gate = new();
    private readonly List<TestActivityEntry> _entries = [];
    private readonly HashSet<string> _sourceNames;
    private readonly int _maximumEntries;
    private readonly ActivityListener _listener;

    /// <summary>Creates a bounded collector, optionally restricted to exact source names.</summary>
    public TestActivityCollector(
        IEnumerable<string>? sourceNames = null,
        int maximumEntries = 1_000)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        _maximumEntries = maximumEntries;
        _sourceNames = new HashSet<string>(sourceNames ?? [], StringComparer.Ordinal);
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                _sourceNames.Count == 0 || _sourceNames.Contains(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Record
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Gets a stable copy of captured completed activities.</summary>
    public IReadOnlyList<TestActivityEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Gets the number of currently captured completed activities.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Starts a fluent assertion chain over captured activities.</summary>
    public TestActivityCollectorAssertions Should() => new(this);

    /// <summary>Removes every captured activity and returns this collector.</summary>
    public TestActivityCollector Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
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
        var entries = Entries.Select(entry => new
        {
            entry.SourceName,
            entry.OperationName,
            entry.DisplayName,
            Kind = entry.Kind.ToString(),
            Status = entry.Status.ToString(),
            entry.StatusDescription,
            TraceId = entry.TraceId.ToString(),
            SpanId = entry.SpanId.ToString(),
            ParentSpanId = entry.ParentSpanId.ToString(),
            entry.StartTime,
            entry.Duration,
            Tags = Stringify(entry.Tags),
            entry.Baggage,
            Events = entry.Events.Select(activityEvent => new
            {
                activityEvent.Name,
                activityEvent.Timestamp,
                Tags = Stringify(activityEvent.Tags)
            }).ToArray()
        }).ToArray();
        return ValueTask.FromResult<object?>(new { Count = entries.Length, Entries = entries });
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

    private void Record(Activity activity)
    {
        var tags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in activity.TagObjects)
        {
            tags[tag.Key] = tag.Value;
        }

        var baggage = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in activity.Baggage)
        {
            baggage[item.Key] = item.Value;
        }

        var events = activity.Events.Select(activityEvent =>
        {
            var eventTags = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in activityEvent.Tags)
            {
                eventTags[tag.Key] = tag.Value;
            }

            return new TestActivityEvent(
                activityEvent.Name,
                activityEvent.Timestamp,
                new ReadOnlyDictionary<string, object?>(eventTags));
        }).ToArray();
        var entry = new TestActivityEntry(
            activity.Source.Name,
            activity.OperationName,
            activity.DisplayName,
            activity.Kind,
            activity.Status,
            activity.StatusDescription,
            activity.TraceId,
            activity.SpanId,
            activity.ParentSpanId,
            activity.StartTimeUtc,
            activity.Duration,
            new ReadOnlyDictionary<string, object?>(tags),
            new ReadOnlyDictionary<string, string?>(baggage),
            events);

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > _maximumEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maximumEntries);
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> Stringify(
        IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(
            property => property.Key,
            property => property.Value?.ToString(),
            StringComparer.Ordinal);
}
