namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently constructs an immutable <see cref="TestUser"/>.</summary>
/// <remarks>This mutable builder is not thread-safe. Each call to <see cref="Build"/> returns a snapshot.</remarks>
public sealed class TestUserBuilder
{
    private readonly List<string> _roles = [];
    private readonly List<TestClaim> _claims = [];
    private readonly List<TestIdentity> _additionalIdentities = [];
    private readonly Dictionary<string, string?> _authenticationProperties = new(StringComparer.Ordinal);
    private string _nameIdentifier = Guid.NewGuid().ToString("N");
    private string _name = "integration-test-user";
    private string _authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme;
    private string _authenticationType = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Targets an application-specific authentication scheme.</summary>
    /// <param name="authenticationScheme">
    /// The non-empty scheme name written to the transported user definition. Registration is not
    /// validated until the application processes a request.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithAuthenticationScheme(string authenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
        return this;
    }

    /// <summary>Sets the authentication type exposed by the resulting claims identity.</summary>
    /// <param name="authenticationType">
    /// The non-empty value returned by the resulting identity's authentication-type property.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithAuthenticationType(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        _authenticationType = authenticationType;
        return this;
    }

    /// <summary>Sets the value exposed through the name-identifier claim.</summary>
    /// <param name="nameIdentifier">The non-empty <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> value.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithNameIdentifier(string nameIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameIdentifier);
        _nameIdentifier = nameIdentifier;
        return this;
    }

    /// <summary>Sets the authenticated user's display name.</summary>
    /// <param name="name">The non-empty <see cref="System.Security.Claims.ClaimTypes.Name"/> value.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Adds a role used by role-based authorization.</summary>
    /// <param name="role">The non-empty role value to append. Duplicate roles are retained.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds multiple roles used by role-based authorization.</summary>
    /// <param name="roles">
    /// The non-null role array to append in order. Every element must be non-empty; duplicate roles
    /// are retained. The builder copies the values and does not own the array.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            WithRole(role);
        }

        return this;
    }

    /// <summary>Adds a claim used by policy-based authorization.</summary>
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Adds another claims identity to the resulting principal.</summary>
    /// <param name="configure">
    /// The callback invoked synchronously once with a new identity builder. The built identity is
    /// snapshotted immediately; the callback and builder are not retained.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithIdentity(Action<TestIdentityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestIdentityBuilder();
        configure(builder);
        _additionalIdentities.Add(builder.Build());
        return this;
    }

    /// <summary>Adds or replaces an authentication-ticket property.</summary>
    /// <param name="key">The non-empty, ordinally compared property key.</param>
    /// <param name="value">
    /// The property value, or <see langword="null"/> to store a property with a null value. Using an
    /// existing key replaces its value.
    /// </param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestUserBuilder WithAuthenticationProperty(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _authenticationProperties[key] = value;
        return this;
    }

    /// <summary>Creates the immutable user definition.</summary>
    /// <returns>
    /// A new user containing snapshots of the builder's roles, claims, identities, and ticket
    /// properties. Later builder changes do not modify the returned user.
    /// </returns>
    public TestUser Build() => new()
    {
        AuthenticationScheme = _authenticationScheme,
        AuthenticationType = _authenticationType,
        NameIdentifier = _nameIdentifier,
        Name = _name,
        Roles = _roles.ToArray(),
        Claims = _claims.ToArray(),
        AdditionalIdentities = _additionalIdentities.ToArray(),
        AuthenticationProperties = new Dictionary<string, string?>(_authenticationProperties)
    };
}
