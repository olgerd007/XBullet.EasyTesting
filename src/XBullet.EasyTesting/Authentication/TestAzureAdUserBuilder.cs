namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently creates an Azure AD-shaped test principal.</summary>
public sealed class TestAzureAdUserBuilder
{
    private readonly List<string> _roles = [];
    private readonly List<string> _scopes = [];
    private readonly List<TestClaim> _claims = [];
    private readonly string _authenticationScheme;
    private string _objectId = Guid.NewGuid().ToString("D");
    private string _tenantId = Guid.NewGuid().ToString("D");
    private string _name = "azure-ad-test-user";
    private string _preferredUsername = "azure-ad-test-user@example.test";
    private string? _clientId;

    /// <summary>Creates an Azure AD test-user builder targeting the supplied application scheme.</summary>
    public TestAzureAdUserBuilder(
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
    }

    /// <summary>Sets the Azure AD object identifier.</summary>
    public TestAzureAdUserBuilder WithObjectId(string objectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        _objectId = objectId;
        return this;
    }

    /// <summary>Sets the Azure AD tenant identifier.</summary>
    public TestAzureAdUserBuilder WithTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Sets the principal's display name.</summary>
    public TestAzureAdUserBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Sets the preferred username claim.</summary>
    public TestAzureAdUserBuilder WithPreferredUsername(string preferredUsername)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredUsername);
        _preferredUsername = preferredUsername;
        return this;
    }

    /// <summary>Sets the authorized-party application identifier.</summary>
    public TestAzureAdUserBuilder WithClientId(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _clientId = clientId;
        return this;
    }

    /// <summary>Adds a delegated permission to the space-delimited <c>scp</c> claim.</summary>
    public TestAzureAdUserBuilder WithScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _scopes.Add(scope);
        return this;
    }

    /// <summary>Adds delegated permissions to the space-delimited <c>scp</c> claim.</summary>
    public TestAzureAdUserBuilder WithScopes(params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        foreach (var scope in scopes)
        {
            WithScope(scope);
        }

        return this;
    }

    /// <summary>Adds an application role recognized by ASP.NET Core role authorization.</summary>
    public TestAzureAdUserBuilder WithAppRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds application roles recognized by ASP.NET Core role authorization.</summary>
    public TestAzureAdUserBuilder WithAppRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            WithAppRole(role);
        }

        return this;
    }

    /// <summary>Adds a custom token claim.</summary>
    public TestAzureAdUserBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the Azure AD-shaped test user.</summary>
    public TestUser Build()
    {
        var claims = new List<TestClaim>
        {
            new(AzureAdClaimTypes.ObjectId, _objectId),
            new(AzureAdClaimTypes.TenantId, _tenantId),
            new(AzureAdClaimTypes.PreferredUsername, _preferredUsername),
            new(
                TestAuthenticationDefaults.AuthenticationMethodClaim,
                TestAuthenticationDefaults.AzureAdAuthenticationMethod)
        };

        if (_scopes.Count > 0)
        {
            claims.Add(new TestClaim(AzureAdClaimTypes.Scope, string.Join(' ', _scopes)));
        }

        if (_clientId is not null)
        {
            claims.Add(new TestClaim(AzureAdClaimTypes.AuthorizedParty, _clientId));
        }

        claims.AddRange(_roles.Select(role => new TestClaim(AzureAdClaimTypes.Roles, role)));
        claims.AddRange(_claims);

        return new TestUser
        {
            AuthenticationScheme = _authenticationScheme,
            AuthenticationType = TestAuthenticationDefaults.AzureAdAuthenticationType,
            NameIdentifier = _objectId,
            Name = _name,
            Roles = _roles.ToArray(),
            Claims = claims.ToArray()
        };
    }
}
