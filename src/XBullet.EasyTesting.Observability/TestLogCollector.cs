using System.Collections.ObjectModel;
using XBullet.EasyTesting.Hosting;
using Microsoft.Extensions.Logging;

namespace XBullet.EasyTesting.Observability;

/// <summary>Thread-safe provider that captures structured application logs in memory.</summary>
public sealed class TestLogCollector : ILoggerProvider, ISupportExternalScope, ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<TestLogEntry> _entries = [];
    private readonly TimeProvider _timeProvider;
    private readonly int _maximumEntries;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    /// <summary>Creates a collector with an optional clock and bounded entry count.</summary>
    public TestLogCollector(TimeProvider? timeProvider = null, int maximumEntries = 1_000)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _maximumEntries = maximumEntries;
    }

    /// <summary>Gets a stable copy of captured entries in recording order.</summary>
    public IReadOnlyList<TestLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Gets the number of currently captured entries.</summary>
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

    /// <summary>Starts a fluent assertion chain over the captured logs.</summary>
    public TestLogCollectorAssertions Should() => new(this);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        return new RecordingLogger(this, categoryName);
    }

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        _scopeProvider = scopeProvider;
    }

    /// <summary>Removes every captured log entry and returns this collector.</summary>
    public TestLogCollector Reset()
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
            entry.Timestamp,
            entry.Category,
            Level = entry.Level.ToString(),
            EventId = entry.EventId.Id,
            entry.EventId.Name,
            entry.Message,
            entry.MessageTemplate,
            State = entry.State.ToDictionary(
                property => property.Key,
                property => property.Value?.ToString(),
                StringComparer.Ordinal),
            Scopes = entry.Scopes.Select(scope => scope?.ToString()).ToArray(),
            ExceptionType = entry.Exception?.GetType().FullName,
            ExceptionMessage = entry.Exception?.Message
        }).ToArray();
        return ValueTask.FromResult<object?>(new { Count = entries.Length, Entries = entries });
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Record<TState>(
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            foreach (var property in structuredState)
            {
                properties[property.Key] = property.Value;
            }
        }
        else if (state is not null)
        {
            properties["State"] = state;
        }

        properties.TryGetValue("{OriginalFormat}", out var messageTemplate);
        var scopes = new List<object?>();
        _scopeProvider.ForEachScope(
            static (scope, target) => target.Add(CaptureScope(scope)),
            scopes);
        var entry = new TestLogEntry(
            _timeProvider.GetUtcNow(),
            category,
            level,
            eventId,
            formatter(state, exception),
            messageTemplate as string,
            new ReadOnlyDictionary<string, object?>(properties),
            scopes.AsReadOnly(),
            exception);

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > _maximumEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maximumEntries);
            }
        }
    }

    private static object? CaptureScope(object? scope)
    {
        if (scope is not IEnumerable<KeyValuePair<string, object?>> structuredScope)
        {
            return scope;
        }

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in structuredScope)
        {
            properties[property.Key] = property.Value;
        }

        return new ReadOnlyDictionary<string, object?>(properties);
    }

    private sealed class RecordingLogger(TestLogCollector owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                owner.Record(category, logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
