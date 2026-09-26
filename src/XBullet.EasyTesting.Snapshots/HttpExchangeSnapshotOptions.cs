namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls the request and response portions of an HTTP exchange snapshot.</summary>
public sealed class HttpExchangeSnapshotOptions
{
    private HttpExchangeSnapshotFormat _format;
    private bool _formatOverridden;
    private bool _includesGlobalDefaults;

    /// <summary>Gets or sets the committed snapshot file format. The default is JSON.</summary>
    public HttpExchangeSnapshotFormat Format
    {
        get => _format;
        set
        {
            _format = value;
            _formatOverridden = true;
        }
    }

    /// <summary>Gets the request snapshot options.</summary>
    public HttpExchangeRequestSnapshotOptions Request { get; } = new();

    /// <summary>Gets the response snapshot options.</summary>
    public HttpExchangeResponseSnapshotOptions Response { get; } = new();

    internal HttpExchangeSnapshotOptions Copy()
    {
        var copy = new HttpExchangeSnapshotOptions
        {
            _format = _format,
            _formatOverridden = _formatOverridden,
            _includesGlobalDefaults = _includesGlobalDefaults
        };
        copy.Request.CopyFrom(Request);
        copy.Response.CopyFrom(Response);
        return copy;
    }

    internal HttpExchangeSnapshotOptions Merge(HttpExchangeSnapshotOptions local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var merged = Copy();
        if (local._formatOverridden)
        {
            merged._format = local._format;
        }

        merged._formatOverridden |= local._formatOverridden;
        merged.Request.MergeFrom(local.Request);
        merged.Response.MergeFrom(local.Response);
        return merged;
    }

    internal bool IncludesGlobalDefaults => _includesGlobalDefaults;

    internal void MarkIncludesGlobalDefaults() => _includesGlobalDefaults = true;
}

/// <summary>Controls which HTTP request details are included in an exchange snapshot.</summary>
public sealed class HttpExchangeRequestSnapshotOptions
{
    private readonly HashSet<string> _explicitHeaderDecisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitQueryParameterDecisions = new(StringComparer.OrdinalIgnoreCase);
    private bool _includeHeaders = true;
    private bool _includeBody = true;
    private OptionOverrides _overrides;

    /// <summary>Gets or sets whether request headers are included.</summary>
    public bool IncludeHeaders
    {
        get => _includeHeaders;
        set
        {
            _includeHeaders = value;
            _overrides |= OptionOverrides.IncludeHeaders;
        }
    }

    /// <summary>Gets or sets whether request content is included.</summary>
    public bool IncludeBody
    {
        get => _includeBody;
        set
        {
            _includeBody = value;
            _overrides |= OptionOverrides.IncludeBody;
        }
    }

    /// <summary>Gets request headers excluded from snapshots.</summary>
    public ISet<string> IgnoredHeaders { get; } = CreateIgnoredHeaders();

    /// <summary>Gets request headers whose values are replaced with <c>{Redacted}</c>.</summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets query parameters whose values are replaced with <c>{Redacted}</c>.</summary>
    public ISet<string> RedactedQueryParameters { get; } = SensitiveQueryParameterDefaults.Create();

    /// <summary>
    /// Gets query parameters whose values are replaced with <c>{Scrubbed}</c>. Security redaction
    /// takes precedence when a name appears in both query-parameter sets.
    /// </summary>
    public ISet<string> ScrubbedQueryParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal List<Func<string, string>> UrlPathScrubbers { get; } = [];

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
            _explicitHeaderDecisions.Add(headerName);
        }

        return this;
    }

    /// <summary>Includes a request header that was excluded by default.</summary>
    public HttpExchangeRequestSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        _explicitHeaderDecisions.Add(headerName);
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
            _explicitHeaderDecisions.Add(headerName);
        }

        return this;
    }

    /// <summary>Redacts one query-parameter value and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions RedactingQueryParameter(string parameterName) =>
        RedactingQueryParameters(parameterName);

    /// <summary>Redacts query-parameter values and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions RedactingQueryParameters(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        foreach (var parameterName in parameterNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
            RedactedQueryParameters.Add(parameterName);
            ScrubbedQueryParameters.Remove(parameterName);
            _explicitQueryParameterDecisions.Add(parameterName);
        }

        return this;
    }

    /// <summary>Scrubs one volatile query-parameter value and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions ScrubbingQueryParameter(string parameterName) =>
        ScrubbingQueryParameters(parameterName);

    /// <summary>Scrubs volatile query-parameter values and returns this instance.</summary>
    public HttpExchangeRequestSnapshotOptions ScrubbingQueryParameters(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        foreach (var parameterName in parameterNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
            if (!RedactedQueryParameters.Contains(parameterName))
            {
                ScrubbedQueryParameters.Add(parameterName);
            }
            _explicitQueryParameterDecisions.Add(parameterName);
        }

        return this;
    }

    /// <summary>Adds a transformation applied to the request URL path before it is snapshotted.</summary>
    public HttpExchangeRequestSnapshotOptions ScrubbingUrlPath(Func<string, string> scrubber)
    {
        ArgumentNullException.ThrowIfNull(scrubber);
        UrlPathScrubbers.Add(scrubber);
        return this;
    }

    /// <summary>Replaces complete GUID request-path segments with <c>{Guid}</c>.</summary>
    public HttpExchangeRequestSnapshotOptions ScrubbingUrlPathGuids() =>
        ScrubbingUrlPath(SnapshotUrlFormatter.ScrubGuidsInPath);

    /// <summary>Includes the original value of a query parameter.</summary>
    public HttpExchangeRequestSnapshotOptions IncludingQueryParameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        RedactedQueryParameters.Remove(parameterName);
        ScrubbedQueryParameters.Remove(parameterName);
        _explicitQueryParameterDecisions.Add(parameterName);
        return this;
    }

    internal void CopyFrom(HttpExchangeRequestSnapshotOptions source)
    {
        _includeHeaders = source._includeHeaders;
        _includeBody = source._includeBody;
        _overrides = source._overrides;
        IgnoredHeaders.Clear();
        IgnoredHeaders.UnionWith(source.IgnoredHeaders);
        RedactedHeaders.Clear();
        RedactedHeaders.UnionWith(source.RedactedHeaders);
        RedactedQueryParameters.Clear();
        RedactedQueryParameters.UnionWith(source.RedactedQueryParameters);
        ScrubbedQueryParameters.Clear();
        ScrubbedQueryParameters.UnionWith(source.ScrubbedQueryParameters);
        UrlPathScrubbers.Clear();
        UrlPathScrubbers.AddRange(source.UrlPathScrubbers);
        _explicitHeaderDecisions.Clear();
        _explicitHeaderDecisions.UnionWith(source._explicitHeaderDecisions);
        _explicitQueryParameterDecisions.Clear();
        _explicitQueryParameterDecisions.UnionWith(source._explicitQueryParameterDecisions);
    }

    internal void MergeFrom(HttpExchangeRequestSnapshotOptions local)
    {
        if (local._overrides.HasFlag(OptionOverrides.IncludeHeaders))
        {
            _includeHeaders = local._includeHeaders;
        }
        if (local._overrides.HasFlag(OptionOverrides.IncludeBody))
        {
            _includeBody = local._includeBody;
        }
        _overrides |= local._overrides;

        var defaultIgnoredHeaders = CreateIgnoredHeaders();
        var headerDecisions = new HashSet<string>(
            local._explicitHeaderDecisions,
            StringComparer.OrdinalIgnoreCase);
        headerDecisions.UnionWith(local.RedactedHeaders);
        headerDecisions.UnionWith(local.IgnoredHeaders.Where(
            header => !defaultIgnoredHeaders.Contains(header)));
        headerDecisions.UnionWith(defaultIgnoredHeaders.Where(
            header => !local.IgnoredHeaders.Contains(header)));
        foreach (var headerName in headerDecisions)
        {
            if (local.RedactedHeaders.Contains(headerName))
            {
                IgnoredHeaders.Remove(headerName);
                RedactedHeaders.Add(headerName);
            }
            else if (local.IgnoredHeaders.Contains(headerName))
            {
                IgnoredHeaders.Add(headerName);
                RedactedHeaders.Remove(headerName);
            }
            else
            {
                IgnoredHeaders.Remove(headerName);
                RedactedHeaders.Remove(headerName);
            }
        }

        var defaultQueryParameters = SensitiveQueryParameterDefaults.Create();
        var queryParameterDecisions = new HashSet<string>(
            local._explicitQueryParameterDecisions,
            StringComparer.OrdinalIgnoreCase);
        queryParameterDecisions.UnionWith(local.RedactedQueryParameters.Where(
            parameter => !defaultQueryParameters.Contains(parameter)));
        queryParameterDecisions.UnionWith(defaultQueryParameters.Where(
            parameter => !local.RedactedQueryParameters.Contains(parameter)));
        queryParameterDecisions.UnionWith(local.ScrubbedQueryParameters);
        foreach (var parameterName in queryParameterDecisions)
        {
            if (local.RedactedQueryParameters.Contains(parameterName))
            {
                RedactedQueryParameters.Add(parameterName);
                ScrubbedQueryParameters.Remove(parameterName);
            }
            else if (local.ScrubbedQueryParameters.Contains(parameterName))
            {
                RedactedQueryParameters.Remove(parameterName);
                ScrubbedQueryParameters.Add(parameterName);
            }
            else
            {
                RedactedQueryParameters.Remove(parameterName);
                ScrubbedQueryParameters.Remove(parameterName);
            }
        }

        UrlPathScrubbers.AddRange(local.UrlPathScrubbers);
        _explicitHeaderDecisions.UnionWith(headerDecisions);
        _explicitQueryParameterDecisions.UnionWith(queryParameterDecisions);
    }

    private static HashSet<string> CreateIgnoredHeaders() =>
        new(StringComparer.OrdinalIgnoreCase)
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

    [Flags]
    private enum OptionOverrides
    {
        None = 0,
        IncludeHeaders = 1 << 0,
        IncludeBody = 1 << 1
    }
}

/// <summary>Controls which HTTP response details are included in an exchange snapshot.</summary>
public sealed class HttpExchangeResponseSnapshotOptions
{
    private readonly HashSet<string> _explicitHeaderDecisions = new(StringComparer.OrdinalIgnoreCase);
    private bool _includeHeaders = true;
    private bool _includeBody = true;
    private OptionOverrides _overrides;

    /// <summary>Gets or sets whether response headers are included.</summary>
    public bool IncludeHeaders
    {
        get => _includeHeaders;
        set
        {
            _includeHeaders = value;
            _overrides |= OptionOverrides.IncludeHeaders;
        }
    }

    /// <summary>Gets or sets whether response content is included.</summary>
    public bool IncludeBody
    {
        get => _includeBody;
        set
        {
            _includeBody = value;
            _overrides |= OptionOverrides.IncludeBody;
        }
    }

    /// <summary>Gets response headers excluded from snapshots.</summary>
    public ISet<string> IgnoredHeaders { get; } = CreateIgnoredHeaders();

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
            _explicitHeaderDecisions.Add(headerName);
        }

        return this;
    }

    /// <summary>Includes a response header that was excluded by default.</summary>
    public HttpExchangeResponseSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        _explicitHeaderDecisions.Add(headerName);
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
            _explicitHeaderDecisions.Add(headerName);
        }

        return this;
    }

    internal void CopyFrom(HttpExchangeResponseSnapshotOptions source)
    {
        _includeHeaders = source._includeHeaders;
        _includeBody = source._includeBody;
        _overrides = source._overrides;
        IgnoredHeaders.Clear();
        IgnoredHeaders.UnionWith(source.IgnoredHeaders);
        RedactedHeaders.Clear();
        RedactedHeaders.UnionWith(source.RedactedHeaders);
        _explicitHeaderDecisions.Clear();
        _explicitHeaderDecisions.UnionWith(source._explicitHeaderDecisions);
    }

    internal void MergeFrom(HttpExchangeResponseSnapshotOptions local)
    {
        if (local._overrides.HasFlag(OptionOverrides.IncludeHeaders))
        {
            _includeHeaders = local._includeHeaders;
        }
        if (local._overrides.HasFlag(OptionOverrides.IncludeBody))
        {
            _includeBody = local._includeBody;
        }
        _overrides |= local._overrides;

        var defaultIgnoredHeaders = CreateIgnoredHeaders();
        var headerDecisions = new HashSet<string>(
            local._explicitHeaderDecisions,
            StringComparer.OrdinalIgnoreCase);
        headerDecisions.UnionWith(local.RedactedHeaders);
        headerDecisions.UnionWith(local.IgnoredHeaders.Where(
            header => !defaultIgnoredHeaders.Contains(header)));
        headerDecisions.UnionWith(defaultIgnoredHeaders.Where(
            header => !local.IgnoredHeaders.Contains(header)));
        foreach (var headerName in headerDecisions)
        {
            if (local.RedactedHeaders.Contains(headerName))
            {
                IgnoredHeaders.Remove(headerName);
                RedactedHeaders.Add(headerName);
            }
            else if (local.IgnoredHeaders.Contains(headerName))
            {
                IgnoredHeaders.Add(headerName);
                RedactedHeaders.Remove(headerName);
            }
            else
            {
                IgnoredHeaders.Remove(headerName);
                RedactedHeaders.Remove(headerName);
            }
        }

        _explicitHeaderDecisions.UnionWith(headerDecisions);
    }

    private static HashSet<string> CreateIgnoredHeaders() =>
        new(StringComparer.OrdinalIgnoreCase)
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

    [Flags]
    private enum OptionOverrides
    {
        None = 0,
        IncludeHeaders = 1 << 0,
        IncludeBody = 1 << 1
    }
}
