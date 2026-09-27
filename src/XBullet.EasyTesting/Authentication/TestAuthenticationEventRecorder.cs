using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Records redacted authentication events and exposes fluent assertions.</summary>
/// <remarks>
/// Events may be recorded concurrently. Each read returns a point-in-time array snapshot; a reset
/// does not prevent events from being added concurrently afterward. JWT-shaped values in captured
/// failure messages are replaced before storage.
/// </remarks>
public sealed partial class TestAuthenticationEventRecorder : ITestScenarioResource
{
    private readonly ConcurrentQueue<TestAuthenticationEvent> _events = new();

    /// <summary>Gets a point-in-time copy of recorded authentication events.</summary>
    /// <value>
    /// A newly allocated, read-only array snapshot in recording order. The caller may retain the
    /// snapshot; later recordings and resets do not change it.
    /// </value>
    public IReadOnlyList<TestAuthenticationEvent> Events => _events.ToArray();

    /// <summary>Starts a fluent assertion chain over the recorded events.</summary>
    /// <returns>
    /// A new assertion object that references this recorder and evaluates its current event
    /// snapshot each time an assertion runs.
    /// </returns>
    public TestAuthenticationEventAssertions Should() => new(this);

    /// <summary>Clears all recorded events.</summary>
    public void Reset() => _events.Clear();

    internal void Record(
        TestAuthenticationEventKind kind,
        string authenticationScheme,
        string method,
        string path,
        Exception? failure = null)
    {
        _events.Enqueue(new TestAuthenticationEvent(
            kind,
            authenticationScheme,
            method,
            path,
            DateTimeOffset.UtcNow,
            failure?.GetType().FullName,
            failure is null ? null : Redact(failure.Message)));
    }

    /// <summary>Clears all authentication events currently held by the recorder.</summary>
    /// <param name="cancellationToken">
    /// Accepted for the resource contract but not observed because reset completes synchronously.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>A value task that is already complete after the events have been cleared.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <summary>Captures the currently recorded authentication events for diagnostics.</summary>
    /// <param name="cancellationToken">
    /// Accepted for the resource contract but not observed because capture completes synchronously.
    /// The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// An already-completed value task containing a newly allocated event-array snapshot. The
    /// caller may retain it; subsequent recordings and resets do not change the snapshot.
    /// </returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<object?>(Events);

    internal static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return JwtPattern().Replace(value, "[REDACTED_TOKEN]");
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9_-])")]
    private static partial Regex JwtPattern();
}
