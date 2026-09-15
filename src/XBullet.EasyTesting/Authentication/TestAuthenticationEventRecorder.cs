using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Records redacted authentication events and exposes fluent assertions.</summary>
public sealed partial class TestAuthenticationEventRecorder : ITestScenarioResource
{
    private readonly ConcurrentQueue<TestAuthenticationEvent> _events = new();

    /// <summary>Gets a point-in-time copy of recorded authentication events.</summary>
    public IReadOnlyList<TestAuthenticationEvent> Events => _events.ToArray();

    /// <summary>Starts a fluent assertion chain over the recorded events.</summary>
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

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
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
