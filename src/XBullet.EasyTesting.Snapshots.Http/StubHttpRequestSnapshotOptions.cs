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

    /// <summary>
    /// Gets headers whose presence is captured while their values are replaced with
    /// <c>{Redacted}</c>. Header names are matched without regard to case.
    /// </summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets query parameters whose values are replaced with <c>{Redacted}</c>.
    /// Names are matched without regard to case.
    /// </summary>
    public ISet<string> RedactedQueryParameters { get; } = SensitiveQueryParameterDefaults.Create();

    /// <summary>
    /// Gets query parameters whose values are replaced with <c>{Scrubbed}</c>. Names are matched
    /// without regard to case. Security redaction takes precedence when a name appears in both
    /// query-parameter sets.
    /// </summary>
    public ISet<string> ScrubbedQueryParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal List<Func<string, string>> UrlPathScrubbers { get; } = [];

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
            RedactedHeaders.Remove(headerName);
        }

        return this;
    }

    /// <summary>Includes a header that was excluded by default and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts a header value while preserving the header and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts header values while preserving the headers and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions RedactingHeaders(params string[] headerNames)
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

    /// <summary>Redacts one query-parameter value and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions RedactingQueryParameter(string parameterName) =>
        RedactingQueryParameters(parameterName);

    /// <summary>Redacts query-parameter values and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions RedactingQueryParameters(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        foreach (var parameterName in parameterNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
            RedactedQueryParameters.Add(parameterName);
            ScrubbedQueryParameters.Remove(parameterName);
        }

        return this;
    }

    /// <summary>Scrubs one volatile query-parameter value and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions ScrubbingQueryParameter(string parameterName) =>
        ScrubbingQueryParameters(parameterName);

    /// <summary>Scrubs volatile query-parameter values and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions ScrubbingQueryParameters(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        foreach (var parameterName in parameterNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
            if (!RedactedQueryParameters.Contains(parameterName))
            {
                ScrubbedQueryParameters.Add(parameterName);
            }
        }

        return this;
    }

    /// <summary>Adds a transformation applied to the request URL path before it is snapshotted.</summary>
    public StubHttpRequestSnapshotOptions ScrubbingUrlPath(Func<string, string> scrubber)
    {
        ArgumentNullException.ThrowIfNull(scrubber);
        UrlPathScrubbers.Add(scrubber);
        return this;
    }

    /// <summary>Replaces complete GUID request-path segments with <c>{Guid}</c>.</summary>
    public StubHttpRequestSnapshotOptions ScrubbingUrlPathGuids() =>
        ScrubbingUrlPath(SnapshotUrlFormatter.ScrubGuidsInPath);

    /// <summary>Includes the original value of a query parameter and returns this instance.</summary>
    public StubHttpRequestSnapshotOptions IncludingQueryParameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        RedactedQueryParameters.Remove(parameterName);
        ScrubbedQueryParameters.Remove(parameterName);
        return this;
    }

}
