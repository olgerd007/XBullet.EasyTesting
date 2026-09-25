namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls the request and response portions of an HTTP exchange snapshot.</summary>
public sealed class HttpExchangeSnapshotOptions
{
    /// <summary>Gets the request snapshot options.</summary>
    public HttpExchangeRequestSnapshotOptions Request { get; } = new();

    /// <summary>Gets the response snapshot options.</summary>
    public HttpExchangeResponseSnapshotOptions Response { get; } = new();
}

/// <summary>Controls which HTTP request details are included in an exchange snapshot.</summary>
public sealed class HttpExchangeRequestSnapshotOptions
{
    /// <summary>Gets or sets whether request headers are included.</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Gets or sets whether request content is included.</summary>
    public bool IncludeBody { get; set; } = true;

    /// <summary>Gets request headers excluded from snapshots.</summary>
    public ISet<string> IgnoredHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Request-Id",
        "Traceparent",
        "X-Api-Key",
        "X-Correlation-Id",
        "X-Integration-Test-User",
        "X-XBullet-Test-Client-Certificate"
    };

    /// <summary>Gets request headers whose values are replaced with <c>{Redacted}</c>.</summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets query parameters whose values are replaced with <c>{Redacted}</c>.</summary>
    public ISet<string> RedactedQueryParameters { get; } = SensitiveQueryParameterDefaults.Create();

    /// <summary>Excludes request headers and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes request content and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes request headers and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions IgnoringHeaders(params string[] headerNames)
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

    /// <summary>Includes a request header that was excluded by default.</summary>
    public HttpExchangeRequestSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts a request header value while preserving the header.</summary>
    public HttpExchangeRequestSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts request header values while preserving the headers.</summary>
    public HttpExchangeRequestSnapshotOptions RedactingHeaders(params string[] headerNames)
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

    /// <summary>Redacts query-parameter values and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions RedactingQueryParameters(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        foreach (var parameterName in parameterNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
            RedactedQueryParameters.Add(parameterName);
        }

        return this;
    }

    /// <summary>Includes the original value of a query parameter.</summary>
    public HttpExchangeRequestSnapshotOptions IncludingQueryParameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        RedactedQueryParameters.Remove(parameterName);
        return this;
    }

}

/// <summary>Controls which HTTP response details are included in an exchange snapshot.</summary>
public sealed class HttpExchangeResponseSnapshotOptions
{
    /// <summary>Gets or sets whether response headers are included.</summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Gets or sets whether response content is included.</summary>
    public bool IncludeBody { get; set; } = true;

    /// <summary>Gets response headers excluded from snapshots.</summary>
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

    /// <summary>Gets response headers whose values are replaced with <c>{Redacted}</c>.</summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Excludes response headers and returns this instance.</summary>
    public HttpExchangeResponseSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes response content and returns this instance.</summary>
    public HttpExchangeResponseSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes response headers and returns this instance.</summary>
    public HttpExchangeResponseSnapshotOptions IgnoringHeaders(params string[] headerNames)
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
    public HttpExchangeResponseSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts a response header value while preserving the header.</summary>
    public HttpExchangeResponseSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts response header values while preserving the headers.</summary>
    public HttpExchangeResponseSnapshotOptions RedactingHeaders(params string[] headerNames)
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
