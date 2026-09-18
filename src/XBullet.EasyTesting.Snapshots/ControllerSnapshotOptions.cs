namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which HTTP response details are included in a controller snapshot.</summary>
public sealed class ControllerSnapshotOptions
{
    /// <summary>Gets or sets whether the request method and relative URL are included.</summary>
    public bool IncludeRequest { get; set; } = true;

    /// <summary>Gets or sets whether response content is included.</summary>
    public bool IncludeBody { get; set; } = true;

    /// <summary>Gets or sets whether response headers are included.</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>
    /// Gets headers excluded from snapshots. Volatile infrastructure headers are excluded by default.
    /// Header names are matched without regard to case.
    /// </summary>
    public ISet<string> IgnoredHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Date",
        "Server",
        "Set-Cookie",
        "Authentication-Info",
        "Proxy-Authentication-Info",
        "Request-Id",
        "Traceparent",
        "X-Correlation-Id"
    };

    /// <summary>
    /// Gets headers whose presence is captured while their values are replaced with
    /// <c>{Redacted}</c>. Header names are matched without regard to case.
    /// </summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Excludes request details and returns this instance.</summary>
    public ControllerSnapshotOptions WithoutRequest()
    {
        IncludeRequest = false;
        return this;
    }

    /// <summary>Excludes the response body and returns this instance.</summary>
    public ControllerSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes all response headers and returns this instance.</summary>
    public ControllerSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes response headers and returns this instance.</summary>
    public ControllerSnapshotOptions IgnoringHeaders(params string[] headerNames)
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

    /// <summary>Includes a header that was excluded by default and returns this instance.</summary>
    public ControllerSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts response header values while preserving the header and returns this instance.</summary>
    public ControllerSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts response header values while preserving the headers and returns this instance.</summary>
    public ControllerSnapshotOptions RedactingHeaders(params string[] headerNames)
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
