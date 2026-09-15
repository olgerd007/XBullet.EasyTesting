namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which outbound HTTP request details are included in snapshots.</summary>
public sealed class StubHttpRequestSnapshotOptions
{
    /// <summary>Gets or sets whether request headers are included.</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Gets or sets whether request content is included.</summary>
    public bool IncludeBody { get; set; } = true;

    /// <summary>
    /// Gets headers excluded from snapshots. Credentials and volatile tracing headers are excluded by default.
    /// Header names are matched without regard to case.
    /// </summary>
    public ISet<string> IgnoredHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Request-Id",
        "Traceparent",
        "X-Api-Key",
        "X-Correlation-Id"
    };

    /// <summary>Excludes request headers and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes request content and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes headers and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions IgnoringHeaders(params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(headerNames);
        foreach (var headerName in headerNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
            IgnoredHeaders.Add(headerName);
        }

        return this;
    }

    /// <summary>Includes a header that was excluded by default and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        return this;
    }
}
