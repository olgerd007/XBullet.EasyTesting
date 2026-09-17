namespace XBullet.EasyTesting.Authentication;

/// <summary>
/// Fluently creates a test principal with a primary Federation identity and an additional API-user
/// identity.
/// </summary>
public sealed class TestFederatedUserBuilder
{
    private readonly List<string> _roles = [];
    private readonly List<TestClaim> _federationClaims = [];
    private readonly Dictionary<string, string?> _authenticationProperties =
        new(StringComparer.Ordinal);
    private readonly string _authenticationScheme;
    private readonly TestIdentityBuilder _apiUserIdentity = new TestIdentityBuilder()
        .WithAuthenticationType(TestAuthenticationDefaults.ApiUserIdentityAuthenticationType);
    private string _nameIdentifier = Guid.NewGuid().ToString("N");
    private string _name = "federated-test-user";
    private string _federationAuthenticationType =
        TestAuthenticationDefaults.FederationAuthenticationType;

    /// <summary>Creates a federated test-user builder targeting the supplied application scheme.</summary>
    public TestFederatedUserBuilder(
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
    }

    /// <summary>Sets the subject identifier exposed by the primary identity.</summary>
    public TestFederatedUserBuilder WithNameIdentifier(string nameIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameIdentifier);
        _nameIdentifier = nameIdentifier;
        return this;
    }

    /// <summary>Sets the name exposed by the primary identity.</summary>
    public TestFederatedUserBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Overrides the authentication type used for the Federation identity.</summary>
    public TestFederatedUserBuilder WithFederationAuthenticationType(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        _federationAuthenticationType = authenticationType;
        return this;
    }

    /// <summary>Adds a role to the primary Federation identity.</summary>
    public TestFederatedUserBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds roles to the primary Federation identity.</summary>
    public TestFederatedUserBuilder WithRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            WithRole(role);
        }

        return this;
    }

    /// <summary>Adds a claim to the primary Federation identity.</summary>
    public TestFederatedUserBuilder WithFederationClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _federationClaims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Configures the additional API-user identity.</summary>
    public TestFederatedUserBuilder WithApiUserIdentity(Action<TestIdentityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_apiUserIdentity);
        return this;
    }

    /// <summary>Adds a claim to the additional API-user identity.</summary>
    public TestFederatedUserBuilder WithApiUserClaim(string type, string value)
    {
        _apiUserIdentity.WithClaim(type, value);
        return this;
    }

    /// <summary>Adds or replaces an authentication-ticket property.</summary>
    public TestFederatedUserBuilder WithAuthenticationProperty(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _authenticationProperties[key] = value;
        return this;
    }

    /// <summary>Creates the Federation/API-user test principal definition.</summary>
    public TestUser Build() => new()
    {
        AuthenticationScheme = _authenticationScheme,
        AuthenticationType = _federationAuthenticationType,
        NameIdentifier = _nameIdentifier,
        Name = _name,
        Roles = _roles.ToArray(),
        Claims = _federationClaims.ToArray(),
        AdditionalIdentities = [_apiUserIdentity.Build()],
        AuthenticationProperties = new Dictionary<string, string?>(_authenticationProperties)
    };
}
