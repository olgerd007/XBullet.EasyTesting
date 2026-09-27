namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently creates an Azure AD-shaped test principal.</summary>
/// <remarks>This mutable builder is not thread-safe. Each call to <see cref="Build"/> returns a snapshot.</remarks>
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
    /// <param name="authenticationScheme">
    /// The non-empty simulated scheme targeted by the resulting user. When omitted, the default
    /// XBullet test-authentication scheme is used.
    /// </param>
    public TestAzureAdUserBuilder(
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
    }

    /// <summary>Sets the Azure AD object identifier.</summary>
    /// <param name="objectId">
    /// The non-empty <c>oid</c> claim and primary name-identifier value. The value is not required to
    /// use GUID syntax.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithObjectId(string objectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        _objectId = objectId;
        return this;
    }

    /// <summary>Sets the Azure AD tenant identifier.</summary>
    /// <param name="tenantId">
    /// The non-empty <c>tid</c> claim value. The value is not required to use GUID syntax.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Sets the principal's display name.</summary>
    /// <param name="name">The non-empty primary name-claim value.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Sets the preferred username claim.</summary>
    /// <param name="preferredUsername">
    /// The non-empty <c>preferred_username</c> claim value. Email-address syntax is not required.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithPreferredUsername(string preferredUsername)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredUsername);
        _preferredUsername = preferredUsername;
        return this;
    }

    /// <summary>Sets the authorized-party application identifier.</summary>
    /// <param name="clientId">
    /// The non-empty <c>azp</c> claim value. The value is not required to use GUID syntax.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithClientId(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _clientId = clientId;
        return this;
    }

    /// <summary>Adds a delegated permission to the space-delimited <c>scp</c> claim.</summary>
    /// <param name="scope">
    /// The non-empty scope value to append. Values are joined with a single space when built;
    /// duplicates are retained.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _scopes.Add(scope);
        return this;
    }

    /// <summary>Adds delegated permissions to the space-delimited <c>scp</c> claim.</summary>
    /// <param name="scopes">
    /// The non-null scope array appended in order. Every element must be non-empty; duplicates are
    /// retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
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
    /// <param name="role">
    /// The non-empty role added both to role authorization and as a <c>roles</c> claim. Duplicates
    /// are retained.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithAppRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds application roles recognized by ASP.NET Core role authorization.</summary>
    /// <param name="roles">
    /// The non-null role array appended in order. Every element must be non-empty; duplicates are
    /// retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
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
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestAzureAdUserBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the Azure AD-shaped test user.</summary>
    /// <returns>
    /// A new user containing snapshots of the configured identifiers, scopes, roles, and custom
    /// claims. Later builder changes do not modify the returned user.
    /// </returns>
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
