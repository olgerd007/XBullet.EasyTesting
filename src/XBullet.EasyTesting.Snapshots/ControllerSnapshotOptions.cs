namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which HTTP response details are included in a controller snapshot.</summary>
public sealed class ControllerSnapshotOptions
{
    private readonly HashSet<string> _explicitHeaderDecisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitQueryParameterDecisions = new(StringComparer.OrdinalIgnoreCase);
    private bool _includeRequest = true;
    private bool _includeBody = true;
    private bool _includeHeaders = true;
    private OptionOverrides _overrides;
    private bool _includesGlobalDefaults;

    /// <summary>Gets or sets whether the request method and relative URL are included.</summary>
    public bool IncludeRequest
    {
        get => _includeRequest;
        set
        {
            _includeRequest = value;
            _overrides |= OptionOverrides.IncludeRequest;
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

    /// <summary>
    /// Gets headers excluded from snapshots. Volatile infrastructure headers are excluded by default.
    /// Header names are matched without regard to case.
    /// </summary>
    public ISet<string> IgnoredHeaders { get; } = CreateIgnoredHeaders();

    /// <summary>
    /// Gets headers whose presence is captured while their values are replaced with
    /// <c>{Redacted}</c>. Header names are matched without regard to case.
    /// </summary>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets request query parameters whose values are replaced with <c>{Redacted}</c>.
    /// Names are matched without regard to case.
    /// </summary>
    public ISet<string> RedactedQueryParameters { get; } = SensitiveQueryParameterDefaults.Create();

    /// <summary>
    /// Gets request query parameters whose values are replaced with <c>{Scrubbed}</c>.
    /// Names are matched without regard to case. Security redaction takes precedence when a name
    /// appears in both query-parameter sets.
    /// </summary>
    public ISet<string> ScrubbedQueryParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal List<Func<string, string>> UrlPathScrubbers { get; } = [];

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
            _explicitHeaderDecisions.Add(headerName);
        }

        return this;
    }

    /// <summary>Includes a header that was excluded by default and returns this instance.</summary>
    public ControllerSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        _explicitHeaderDecisions.Add(headerName);
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
            _explicitHeaderDecisions.Add(headerName);
        }

        return this;
    }

    /// <summary>Redacts one request query-parameter value and returns this instance.</summary>
    public ControllerSnapshotOptions RedactingQueryParameter(string parameterName) =>
        RedactingQueryParameters(parameterName);

    /// <summary>Redacts request query-parameter values and returns this instance.</summary>
    public ControllerSnapshotOptions RedactingQueryParameters(params string[] parameterNames)
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

    /// <summary>Scrubs one volatile request query-parameter value and returns this instance.</summary>
    public ControllerSnapshotOptions ScrubbingQueryParameter(string parameterName) =>
        ScrubbingQueryParameters(parameterName);

    /// <summary>Scrubs volatile request query-parameter values and returns this instance.</summary>
    public ControllerSnapshotOptions ScrubbingQueryParameters(params string[] parameterNames)
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
    public ControllerSnapshotOptions ScrubbingUrlPath(Func<string, string> scrubber)
    {
        ArgumentNullException.ThrowIfNull(scrubber);
        UrlPathScrubbers.Add(scrubber);
        return this;
    }

    /// <summary>Replaces complete GUID request-path segments with <c>{Guid}</c>.</summary>
    public ControllerSnapshotOptions ScrubbingUrlPathGuids() =>
        ScrubbingUrlPath(SnapshotUrlFormatter.ScrubGuidsInPath);

    /// <summary>Includes the original value of a query parameter and returns this instance.</summary>
    public ControllerSnapshotOptions IncludingQueryParameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        RedactedQueryParameters.Remove(parameterName);
        ScrubbedQueryParameters.Remove(parameterName);
        _explicitQueryParameterDecisions.Add(parameterName);
        return this;
    }

    internal ControllerSnapshotOptions Copy()
    {
        var copy = new ControllerSnapshotOptions
        {
            _includeRequest = _includeRequest,
            _includeBody = _includeBody,
            _includeHeaders = _includeHeaders,
            _overrides = _overrides,
            _includesGlobalDefaults = _includesGlobalDefaults
        };

        copy.IgnoredHeaders.Clear();
        copy.IgnoredHeaders.UnionWith(IgnoredHeaders);
        copy.RedactedHeaders.UnionWith(RedactedHeaders);
        copy.RedactedQueryParameters.Clear();
        copy.RedactedQueryParameters.UnionWith(RedactedQueryParameters);
        copy.ScrubbedQueryParameters.UnionWith(ScrubbedQueryParameters);
        copy.UrlPathScrubbers.AddRange(UrlPathScrubbers);
        copy._explicitHeaderDecisions.UnionWith(_explicitHeaderDecisions);
        copy._explicitQueryParameterDecisions.UnionWith(_explicitQueryParameterDecisions);
        return copy;
    }

    internal ControllerSnapshotOptions Merge(ControllerSnapshotOptions local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var merged = Copy();

        if (local._overrides.HasFlag(OptionOverrides.IncludeRequest))
        {
            merged._includeRequest = local._includeRequest;
        }
        if (local._overrides.HasFlag(OptionOverrides.IncludeBody))
        {
            merged._includeBody = local._includeBody;
        }
        if (local._overrides.HasFlag(OptionOverrides.IncludeHeaders))
        {
            merged._includeHeaders = local._includeHeaders;
        }
        merged._overrides |= local._overrides;

        var defaultIgnoredHeaders = CreateIgnoredHeaders();
        var headerDecisions = new HashSet<string>(
            local._explicitHeaderDecisions,
            StringComparer.OrdinalIgnoreCase);
        headerDecisions.UnionWith(local.RedactedHeaders);
        headerDecisions.UnionWith(local.IgnoredHeaders.Where(header => !defaultIgnoredHeaders.Contains(header)));
        headerDecisions.UnionWith(defaultIgnoredHeaders.Where(header => !local.IgnoredHeaders.Contains(header)));
        foreach (var headerName in headerDecisions)
        {
            if (local.RedactedHeaders.Contains(headerName))
            {
                merged.IgnoredHeaders.Remove(headerName);
                merged.RedactedHeaders.Add(headerName);
            }
            else if (local.IgnoredHeaders.Contains(headerName))
            {
                merged.IgnoredHeaders.Add(headerName);
                merged.RedactedHeaders.Remove(headerName);
            }
            else
            {
                merged.IgnoredHeaders.Remove(headerName);
                merged.RedactedHeaders.Remove(headerName);
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
                merged.RedactedQueryParameters.Add(parameterName);
                merged.ScrubbedQueryParameters.Remove(parameterName);
            }
            else if (local.ScrubbedQueryParameters.Contains(parameterName))
            {
                merged.RedactedQueryParameters.Remove(parameterName);
                merged.ScrubbedQueryParameters.Add(parameterName);
            }
            else
            {
                merged.RedactedQueryParameters.Remove(parameterName);
                merged.ScrubbedQueryParameters.Remove(parameterName);
            }
        }

        merged.UrlPathScrubbers.AddRange(local.UrlPathScrubbers);

        merged._explicitHeaderDecisions.UnionWith(headerDecisions);
        merged._explicitQueryParameterDecisions.UnionWith(queryParameterDecisions);
        return merged;
    }

    internal bool IncludesGlobalDefaults => _includesGlobalDefaults;

    internal void MarkIncludesGlobalDefaults() => _includesGlobalDefaults = true;

    private static HashSet<string> CreateIgnoredHeaders() =>
        new(StringComparer.OrdinalIgnoreCase)
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

    [Flags]
    private enum OptionOverrides
    {
        None = 0,
        IncludeRequest = 1 << 0,
        IncludeBody = 1 << 1,
        IncludeHeaders = 1 << 2
    }
}
