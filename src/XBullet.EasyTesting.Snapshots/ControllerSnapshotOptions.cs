namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which HTTP response details are included in a controller snapshot.</summary>
public sealed class ControllerSnapshotOptions
{
    /// <summary>Gets or sets whether the request method and relative URL are included.</summary>
    public bool IncludeRequest { get; set; } = true;

    /// <summary>Gets or sets whether response content is included.</summary>
    public bool IncludeBody { get; set; } = true;

    /// <summary>
    /// Gets headers excluded from snapshots. Volatile infrastructure headers are excluded by default.
    /// Header names are matched without regard to case.
    /// </summary>
    public ISet<string> IgnoredHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Date",
        "Server",
        "Request-Id",
        "Traceparent",
        "X-Correlation-Id"
    };

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

    /// <summary>Excludes response headers and returns this instance.</summary>
    public ControllerSnapshotOptions IgnoringHeaders(params string[] headerNames)
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
    public ControllerSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        return this;
    }
}
