namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls which outbound HTTP request details are included in snapshots.</summary>
/// <remarks>
/// This mutable options object is not thread-safe. Its sets and path scrubbers remain live and are
/// read each time a snapshot is created.
/// </remarks>
public sealed class StubHttpRequestSnapshotOptions
{
    /// <summary>Gets or sets whether request headers are included.</summary>
    /// <value><see langword="true"/> to include headers; otherwise, <see langword="false"/>. The default is true.</value>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>Gets or sets whether request content is included.</summary>
    /// <value><see langword="true"/> to include the normalized body; otherwise, <see langword="false"/>. The default is true.</value>
    public bool IncludeBody { get; set; } = true;

    /// <summary>
    /// Gets headers excluded from snapshots. Credentials and volatile tracing headers are excluded by default.
    /// Header names are matched without regard to case.
    /// </summary>
    /// <value>
    /// The live, options-owned set of excluded header names. By default it contains
    /// <c>Authorization</c>, <c>Cookie</c>, <c>Proxy-Authorization</c>, <c>Request-Id</c>,
    /// <c>Traceparent</c>, <c>X-Api-Key</c>, and <c>X-Correlation-Id</c>.
    /// </value>
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
    /// <value>The live, options-owned set of redacted header names. The default set is empty.</value>
    public ISet<string> RedactedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets query parameters whose values are replaced with <c>{Redacted}</c>.
    /// Names are matched without regard to case.
    /// </summary>
    /// <value>
    /// The live, options-owned set of security-sensitive query names. The defaults include common
    /// token, key, password, secret, signature, code, and SAS parameter names.
    /// </value>
    public ISet<string> RedactedQueryParameters { get; } = SensitiveQueryParameterDefaults.Create();

    /// <summary>
    /// Gets query parameters whose values are replaced with <c>{Scrubbed}</c>. Names are matched
    /// without regard to case. Security redaction takes precedence when a name appears in both
    /// query-parameter sets.
    /// </summary>
    /// <value>The live, options-owned set of volatile query names. The default set is empty.</value>
    public ISet<string> ScrubbedQueryParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal List<Func<string, string>> UrlPathScrubbers { get; } = [];

    /// <summary>Excludes request headers and returns this instance.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions WithoutHeaders()
    {
        IncludeHeaders = false;
        return this;
    }

    /// <summary>Excludes request content and returns this instance.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions WithoutBody()
    {
        IncludeBody = false;
        return this;
    }

    /// <summary>Excludes headers and returns this instance.</summary>
    /// <param name="headerNames">
    /// The non-null array of non-empty, case-insensitive names to exclude. An empty array is a
    /// no-op. Values are copied into the ignored set and removed from the redacted set.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
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
    /// <param name="headerName">
    /// The non-empty, case-insensitive name to remove from both the ignored and redacted sets.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions IncludingHeader(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        IgnoredHeaders.Remove(headerName);
        RedactedHeaders.Remove(headerName);
        return this;
    }

    /// <summary>Redacts a header value while preserving the header and returns this instance.</summary>
    /// <param name="headerName">
    /// The non-empty, case-insensitive name to include with every value replaced by
    /// <c>{Redacted}</c>.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions RedactingHeader(string headerName) =>
        RedactingHeaders(headerName);

    /// <summary>Redacts header values while preserving the headers and returns this instance.</summary>
    /// <param name="headerNames">
    /// The non-null array of non-empty, case-insensitive names to include with values replaced by
    /// <c>{Redacted}</c>. An empty array is a no-op. Values are removed from the ignored set.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
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
    /// <param name="parameterName">
    /// The non-empty, case-insensitive decoded query name whose values are replaced by
    /// <c>{Redacted}</c>.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions RedactingQueryParameter(string parameterName) =>
        RedactingQueryParameters(parameterName);

    /// <summary>Redacts query-parameter values and returns this instance.</summary>
    /// <param name="parameterNames">
    /// The non-null array of non-empty, case-insensitive decoded query names to redact. An empty
    /// array is a no-op. Values are copied into the redacted set and removed from the scrubbed set.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
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
    /// <param name="parameterName">
    /// The non-empty, case-insensitive decoded query name whose values are replaced by
    /// <c>{Scrubbed}</c>. Existing security redaction takes precedence.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions ScrubbingQueryParameter(string parameterName) =>
        ScrubbingQueryParameters(parameterName);

    /// <summary>Scrubs volatile query-parameter values and returns this instance.</summary>
    /// <param name="parameterNames">
    /// The non-null array of non-empty, case-insensitive decoded query names to scrub. An empty
    /// array is a no-op. Names already configured for redaction are not added.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
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
    /// <param name="scrubber">
    /// The non-null callback retained and invoked in registration order with the URL path whenever
    /// a snapshot is created. It can run more than once and concurrently when the options are
    /// shared, must return a non-null path, and must not include secrets in thrown exceptions.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions ScrubbingUrlPath(Func<string, string> scrubber)
    {
        ArgumentNullException.ThrowIfNull(scrubber);
        UrlPathScrubbers.Add(scrubber);
        return this;
    }

    /// <summary>Replaces complete GUID request-path segments with <c>{Guid}</c>.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions ScrubbingUrlPathGuids() =>
        ScrubbingUrlPath(SnapshotUrlFormatter.ScrubGuidsInPath);

    /// <summary>Includes the original value of a query parameter and returns this instance.</summary>
    /// <param name="parameterName">
    /// The non-empty, case-insensitive decoded query name to remove from both the redacted and
    /// scrubbed sets. Its original values can therefore appear in snapshots.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public StubHttpRequestSnapshotOptions IncludingQueryParameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        RedactedQueryParameters.Remove(parameterName);
        ScrubbedQueryParameters.Remove(parameterName);
        return this;
    }

}
