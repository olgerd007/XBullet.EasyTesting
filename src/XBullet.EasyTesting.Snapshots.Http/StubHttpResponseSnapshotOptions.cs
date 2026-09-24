namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which outbound HTTP response details are included in exchange snapshots.</summary>
public sealed class StubHttpResponseSnapshotOptions
{
    /// <summary>Gets or sets whether response headers are included.</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Gets or sets whether response content is included.</summary>
    public bool IncludeBody { get; set; } = true;

    /// <summary>Gets headers excluded from snapshots.</summary>
    public ISet<string> IgnoredHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authentication-Info",
        "Date",
        "Proxy-Authentication-Info",
        "Request-Id",
        "Server",
        "Set-Cookie",
        "Traceparent",
        "X-Correlation-Id"
    };

    /// <summary>Gets headers whose values are replaced with <c>{Redacted}</c>.</summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Excludes response headers and returns this instance.</summary>
    public StubHttpResponseSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes response content and returns this instance.</summary>
    public StubHttpResponseSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes response headers and returns this instance.</summary>
    public StubHttpResponseSnapshotOptions IgnoringHeaders(params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(headerNames);
        foreach (var headerName in headerNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
            IgnoredHeaders.Add(headerName);
            RedactedHeaders.Remove(headerName);
        }

        return this;
    }

    /// <summary>Includes a response header that was excluded by default.</summary>
    public StubHttpResponseSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts a response header value while preserving the header.</summary>
    public StubHttpResponseSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts response header values while preserving the headers.</summary>
    public StubHttpResponseSnapshotOptions RedactingHeaders(params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(headerNames);
        foreach (var headerName in headerNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
            IgnoredHeaders.Remove(headerName);
            RedactedHeaders.Add(headerName);
        }

        return this;
    }
}
