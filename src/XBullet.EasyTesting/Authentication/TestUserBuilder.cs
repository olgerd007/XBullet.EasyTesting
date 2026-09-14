namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently constructs an immutable <see cref="TestUser"/>.</summary>
public sealed class TestUserBuilder
{
    private readonly List<string> _roles = [];
    private readonly List<TestClaim> _claims = [];
    private string _nameIdentifier = Guid.NewGuid().ToString("N");
    private string _name = "integration-test-user";
    private string _authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme;
    private string _authenticationType = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Targets an application-specific authentication scheme.</summary>
    public TestUserBuilder WithAuthenticationScheme(string authenticationScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        _authenticationScheme = authenticationScheme;
        return this;
    }

    /// <summary>Sets the authentication type exposed by the resulting claims identity.</summary>
    public TestUserBuilder WithAuthenticationType(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        _authenticationType = authenticationType;
        return this;
    }

    /// <summary>Sets the value exposed through the name-identifier claim.</summary>
    public TestUserBuilder WithNameIdentifier(string nameIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameIdentifier);
        _nameIdentifier = nameIdentifier;
        return this;
    }

    /// <summary>Sets the authenticated user's display name.</summary>
    public TestUserBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Adds a role used by role-based authorization.</summary>
    public TestUserBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _roles.Add(role);
        return this;
    }

    /// <summary>Adds multiple roles used by role-based authorization.</summary>
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
    public TestUserBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the immutable user definition.</summary>
    public TestUser Build() => new()
    {
        AuthenticationScheme = _authenticationScheme,
        AuthenticationType = _authenticationType,
        NameIdentifier = _nameIdentifier,
        Name = _name,
        Roles = _roles.ToArray(),
        Claims = _claims.ToArray()
    };
}
