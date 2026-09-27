namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which outbound HTTP response details are included in exchange snapshots.</summary>
/// <remarks>This mutable options object is not thread-safe. Its header sets remain live.</remarks>
public sealed class StubHttpResponseSnapshotOptions
{
    /// <summary>Gets or sets whether response headers are included.</summary>
    /// <value><see langword="true"/> to include headers; otherwise, <see langword="false"/>. The default is true.</value>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Gets or sets whether response content is included.</summary>
    /// <value><see langword="true"/> to include the normalized body; otherwise, <see langword="false"/>. The default is true.</value>
    public bool IncludeBody { get; set; } = true;

    /// <summary>Gets headers excluded from snapshots.</summary>
    /// <value>
    /// The live, options-owned case-insensitive set. By default it excludes authentication-info,
    /// date, server, cookie, tracing, request, and correlation headers that are sensitive or
    /// volatile.
    /// </value>
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
    /// <value>The live, options-owned case-insensitive set. The default set is empty.</value>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Excludes response headers and returns this instance.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpResponseSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes response content and returns this instance.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpResponseSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes response headers and returns this instance.</summary>
    /// <param name="headerNames">
    /// The non-null array of non-empty, case-insensitive names to exclude. An empty array is a
    /// no-op. Values are copied into the ignored set and removed from the redacted set.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
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
    /// <param name="headerName">
    /// The non-empty, case-insensitive name to remove from both the ignored and redacted sets.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpResponseSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts a response header value while preserving the header.</summary>
    /// <param name="headerName">
    /// The non-empty, case-insensitive name to include with every value replaced by
    /// <c>{Redacted}</c>.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpResponseSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts response header values while preserving the headers.</summary>
    /// <param name="headerNames">
    /// The non-null array of non-empty, case-insensitive names to include with values replaced by
    /// <c>{Redacted}</c>. An empty array is a no-op. Values are removed from the ignored set.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
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
