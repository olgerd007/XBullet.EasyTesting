namespace XBullet.EasyTesting.Observability;

/// <summary>Configures observability capture for one integration-test scenario.</summary>
public sealed class TestObservabilityOptions
{
    private readonly List<string> _activitySourceNames = [];
    private readonly List<string> _meterNames = [];

    /// <summary>Gets or sets the maximum number of log entries retained in memory.</summary>
    public int MaximumLogEntries { get; set; } = 1_000;

    /// <summary>Gets or sets the maximum number of completed activities retained in memory.</summary>
    public int MaximumActivityEntries { get; set; } = 1_000;

    /// <summary>Gets or sets the maximum number of metric measurements retained in memory.</summary>
    public int MaximumMetricMeasurements { get; set; } = 1_000;

    /// <summary>Gets exact activity source names to capture, or an empty list to capture all.</summary>
    public IReadOnlyList<string> ActivitySourceNames => _activitySourceNames.AsReadOnly();

    /// <summary>Gets exact meter names to capture, or an empty list to capture all.</summary>
    public IReadOnlyList<string> MeterNames => _meterNames.AsReadOnly();

    /// <summary>Gets whether the application's TimeProvider is replaced with deterministic time.</summary>
    public bool UsesFakeTime { get; private set; }

    /// <summary>Gets the initial fake UTC time, or null to use FakeTimeProvider's default.</summary>
    public DateTimeOffset? FakeTimeStart { get; private set; }

    /// <summary>Replaces the application's TimeProvider with deterministic time.</summary>
    public TestObservabilityOptions UseFakeTime(DateTimeOffset? start = null)
    {
        UsesFakeTime = true;
        FakeTimeStart = start;
        return this;
    }

    /// <summary>Restricts activity capture to the supplied exact source name.</summary>
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
