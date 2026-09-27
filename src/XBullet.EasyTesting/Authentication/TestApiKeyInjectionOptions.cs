namespace XBullet.EasyTesting.Authentication;

/// <summary>Configures how real API keys are injected into end-to-end test requests.</summary>
public sealed class TestApiKeyInjectionOptions
{
    /// <summary>Gets or sets the application authentication scheme.</summary>
    /// <value>
    /// The non-empty scheme supplied to
    /// <see cref="TestAuthenticationSchemeBuilder.UseEndToEndApiKey"/>. External callers can read
    /// but cannot replace this value.
    /// </value>
    public string AuthenticationScheme { get; internal set; } = "ApiKey";

    /// <summary>Gets or sets the default API-key header name.</summary>
    /// <value>
    /// The non-empty HTTP header name used by client-side API-key injection. The default is
    /// <c>X-Api-Key</c>.
    /// </value>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>Gets or sets the default API-key query parameter name.</summary>
    /// <value>
    /// The non-empty query-string key used by client-side API-key injection. The default is
    /// <c>api_key</c>.
    /// </value>
    public string QueryParameterName { get; set; } = "api_key";

    /// <summary>Gets or sets whether this scheme becomes the host's default authentication scheme.</summary>
    /// <value>
    /// <see langword="true"/> to make this API-key scheme the host default; otherwise,
    /// <see langword="false"/>. The default is <see langword="false"/>.
    /// </value>
    public bool UseAsDefaultScheme { get; set; }

    /// <summary>Sets the default header name.</summary>
    /// <param name="headerName">
    /// The non-empty HTTP header name used when a client does not specify an override. Syntax is
    /// validated by the HTTP stack when the header is applied.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public TestApiKeyInjectionOptions WithHeaderName(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        HeaderName = headerName;
        return this;
    }

    /// <summary>Sets the default query parameter name.</summary>
    /// <param name="parameterName">
    /// The non-empty query-string key used when a client does not specify an override.
    /// </param>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public TestApiKeyInjectionOptions WithQueryParameterName(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        QueryParameterName = parameterName;
        return this;
    }

    /// <summary>Makes this API-key scheme the host's default authentication scheme.</summary>
    /// <returns>This options instance so additional settings can be configured.</returns>
    public TestApiKeyInjectionOptions AsDefaultScheme()
    {
        UseAsDefaultScheme = true;
        return this;
    }
}
