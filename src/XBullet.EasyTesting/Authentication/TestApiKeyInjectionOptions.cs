namespace XBullet.EasyTesting.Authentication;

/// <summary>Configures how real API keys are injected into end-to-end test requests.</summary>
public sealed class TestApiKeyInjectionOptions
{
    /// <summary>Gets or sets the application authentication scheme.</summary>
    public string AuthenticationScheme { get; internal set; } = "ApiKey";

    /// <summary>Gets or sets the default API-key header name.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>Gets or sets the default API-key query parameter name.</summary>
    public string QueryParameterName { get; set; } = "api_key";

    /// <summary>Gets or sets whether this scheme becomes the host's default authentication scheme.</summary>
    public bool UseAsDefaultScheme { get; set; }

    /// <summary>Sets the default header name.</summary>
    public TestApiKeyInjectionOptions WithHeaderName(string headerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        HeaderName = headerName;
        return this;
    }

    /// <summary>Sets the default query parameter name.</summary>
    public TestApiKeyInjectionOptions WithQueryParameterName(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        QueryParameterName = parameterName;
        return this;
    }

    /// <summary>Makes this API-key scheme the host's default authentication scheme.</summary>
    public TestApiKeyInjectionOptions AsDefaultScheme()
    {
        UseAsDefaultScheme = true;
        return this;
    }
}
