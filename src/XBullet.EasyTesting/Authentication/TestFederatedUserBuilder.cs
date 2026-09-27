namespace XBullet.EasyTesting.Authentication;

/// <summary>
/// Fluently creates a test principal with a primary Federation identity and an additional API-user
/// identity.
/// </summary>
/// <remarks>This mutable builder is not thread-safe. Each call to <see cref="Build"/> returns a snapshot.</remarks>
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
    /// <param name="authenticationScheme">
    /// The non-empty simulated scheme targeted by the resulting principal. When omitted, the
    /// default XBullet test-authentication scheme is used.
    /// </param>
    public TestFederatedUserBuilder(
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
    }

    /// <summary>Sets the subject identifier exposed by the primary identity.</summary>
    /// <param name="nameIdentifier">
    /// The non-empty name-identifier claim value for the primary Federation identity.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithNameIdentifier(string nameIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameIdentifier);
        _nameIdentifier = nameIdentifier;
        return this;
    }

    /// <summary>Sets the name exposed by the primary identity.</summary>
    /// <param name="name">The non-empty name-claim value for the primary Federation identity.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Overrides the authentication type used for the Federation identity.</summary>
    /// <param name="authenticationType">
    /// The non-empty authentication type exposed by the primary identity.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithFederationAuthenticationType(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        _federationAuthenticationType = authenticationType;
        return this;
    }

    /// <summary>Adds a role to the primary Federation identity.</summary>
    /// <param name="role">The non-empty role value to append. Duplicate roles are retained.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds roles to the primary Federation identity.</summary>
    /// <param name="roles">
    /// The non-null role array appended in order. Every element must be non-empty; duplicates are
    /// retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
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
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithFederationClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _federationClaims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Configures the additional API-user identity.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with the builder retained for the additional
    /// API-user identity. Repeated calls modify the same identity builder in call order. The
    /// callback is not retained.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithApiUserIdentity(Action<TestIdentityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_apiUserIdentity);
        return this;
    }

    /// <summary>Adds a claim to the additional API-user identity.</summary>
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithApiUserClaim(string type, string value)
    {
        _apiUserIdentity.WithClaim(type, value);
        return this;
    }

    /// <summary>Adds or replaces an authentication-ticket property.</summary>
    /// <param name="key">The non-empty, ordinally compared property key.</param>
    /// <param name="value">
    /// The property value, or <see langword="null"/> to store a property with a null value. Using an
    /// existing key replaces its value.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestFederatedUserBuilder WithAuthenticationProperty(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _authenticationProperties[key] = value;
        return this;
    }

    /// <summary>Creates the Federation/API-user test principal definition.</summary>
    /// <returns>
    /// A new user containing snapshots of the primary Federation identity, additional API-user
    /// identity, roles, claims, and ticket properties. Later builder changes do not modify it.
    /// </returns>
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
