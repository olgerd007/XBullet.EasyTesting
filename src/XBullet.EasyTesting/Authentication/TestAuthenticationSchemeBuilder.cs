namespace XBullet.EasyTesting.Authentication;

/// <summary>Maps test authentication profiles to scheme names used by the application.</summary>
public sealed class TestAuthenticationSchemeBuilder
{
    private readonly HashSet<string> _additionalSchemes = new(StringComparer.Ordinal);

    internal string AzureAdScheme { get; private set; } = TestAuthenticationDefaults.AuthenticationScheme;

    internal string ApiKeyScheme { get; private set; } = TestAuthenticationDefaults.AuthenticationScheme;

    internal IReadOnlyCollection<string> AdditionalSchemes => _additionalSchemes;

    /// <summary>Maps Azure AD test identities to an application scheme such as <c>Bearer</c>.</summary>
    public TestAuthenticationSchemeBuilder MapAzureAd(string authenticationScheme)
    {
        AzureAdScheme = AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>Maps API-key test identities to the application's API-key authentication scheme.</summary>
    public TestAuthenticationSchemeBuilder MapApiKey(string authenticationScheme)
    {
        ApiKeyScheme = AddScheme(authenticationScheme);
        return this;
    }

    /// <summary>Adds an application-specific authentication scheme handled by test identities.</summary>
    public TestAuthenticationSchemeBuilder MapScheme(string authenticationScheme)
    {
        AddScheme(authenticationScheme);
        return this;
    }

    private string AddScheme(string authenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        if (!string.Equals(
                authenticationScheme,
                TestAuthenticationDefaults.AuthenticationScheme,
                StringComparison.Ordinal))
        {
            _additionalSchemes.Add(authenticationScheme);
        }

        return authenticationScheme;
    }
}
