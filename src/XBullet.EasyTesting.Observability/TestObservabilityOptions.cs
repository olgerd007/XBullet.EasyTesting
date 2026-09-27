using Microsoft.Extensions.Time.Testing;

namespace XBullet.EasyTesting.Observability;

/// <summary>Configures observability capture for one integration-test scenario.</summary>
public sealed class TestObservabilityOptions
{
    private readonly List<string> _activitySourceNames = [];
    private readonly List<string> _meterNames = [];

    /// <summary>Gets or sets the maximum number of log entries retained in memory.</summary>
    /// <value>A positive entry count. The default is 1,000; the oldest entries are discarded first.</value>
    public int MaximumLogEntries { get; set; } = 1_000;

    /// <summary>Gets or sets the maximum number of completed activities retained in memory.</summary>
    /// <value>A positive entry count. The default is 1,000; the oldest activities are discarded first.</value>
    public int MaximumActivityEntries { get; set; } = 1_000;

    /// <summary>Gets or sets the maximum number of metric measurements retained in memory.</summary>
    /// <value>A positive measurement count. The default is 1,000; the oldest values are discarded first.</value>
    public int MaximumMetricMeasurements { get; set; } = 1_000;

    /// <summary>Gets exact activity source names to capture, or an empty list to capture all.</summary>
    /// <value>
    /// A live read-only view of distinct, non-empty names in registration order. Matching is ordinal
    /// and case-sensitive; an empty list captures all activity sources.
    /// </value>
    public IReadOnlyList<string> ActivitySourceNames => _activitySourceNames.AsReadOnly();

    /// <summary>Gets exact meter names to capture, or an empty list to capture all.</summary>
    /// <value>
    /// A live read-only view of distinct, non-empty names in registration order. Matching is ordinal
    /// and case-sensitive; an empty list captures all meters.
    /// </value>
    public IReadOnlyList<string> MeterNames => _meterNames.AsReadOnly();

    /// <summary>Gets whether the application's TimeProvider is replaced with deterministic time.</summary>
    /// <value><see langword="true"/> after <see cref="UseFakeTime"/> is called; otherwise, <see langword="false"/>.</value>
    public bool UsesFakeTime { get; private set; }

    /// <summary>Gets the initial fake UTC time, or null to use FakeTimeProvider's default.</summary>
    /// <value>
    /// The absolute starting instant supplied to <see cref="UseFakeTime"/>, or <see langword="null"/>
    /// to use the fake provider's default start.
    /// </value>
    public DateTimeOffset? FakeTimeStart { get; private set; }

    /// <summary>Replaces the application's TimeProvider with deterministic time.</summary>
    /// <param name="start">
    /// The absolute initial fake time, or <see langword="null"/> to use
    /// <see cref="FakeTimeProvider"/>'s default. Calling this method again replaces the prior value.
    /// </param>
    /// <returns>This options instance, for chaining.</returns>
    public TestObservabilityOptions UseFakeTime(DateTimeOffset? start = null)
    {
        UsesFakeTime = true;
        FakeTimeStart = start;
        return this;
    }

    /// <summary>Restricts activity capture to the supplied exact source name.</summary>
    /// <param name="sourceName">
    /// The non-empty source name to add. Matching is ordinal and case-sensitive; an exact duplicate
    /// is ignored.
    /// </param>
    /// <returns>This options instance, for chaining.</returns>
    public TestObservabilityOptions CaptureActivitySource(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!_activitySourceNames.Contains(sourceName, StringComparer.Ordinal))
        {
            _activitySourceNames.Add(sourceName);
        }

        return this;
    }

    /// <summary>Restricts metric capture to the supplied exact meter name.</summary>
    /// <param name="meterName">
    /// The non-empty meter name to add. Matching is ordinal and case-sensitive; an exact duplicate is ignored.
    /// </param>
    /// <returns>This options instance, for chaining.</returns>
    public TestObservabilityOptions CaptureMeter(string meterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meterName);
        if (!_meterNames.Contains(meterName, StringComparer.Ordinal))
        {
            _meterNames.Add(meterName);
        }

        return this;
    }
}
