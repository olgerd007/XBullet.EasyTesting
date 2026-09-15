namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently constructs an additional identity for a simulated principal.</summary>
public sealed class TestIdentityBuilder
{
    private readonly List<TestClaim> _claims = [];
    private string _authenticationType = TestAuthenticationDefaults.AuthenticationScheme;
    private string _nameClaimType = System.Security.Claims.ClaimTypes.Name;
    private string _roleClaimType = System.Security.Claims.ClaimTypes.Role;

    /// <summary>Sets the identity authentication type.</summary>
    public TestIdentityBuilder WithAuthenticationType(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        _authenticationType = authenticationType;
        return this;
    }

    /// <summary>Sets the claim types used for name and role resolution.</summary>
    public TestIdentityBuilder WithClaimTypes(string nameClaimType, string roleClaimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleClaimType);
        _nameClaimType = nameClaimType;
        _roleClaimType = roleClaimType;
        return this;
    }

    /// <summary>Adds a claim to this identity.</summary>
    public TestIdentityBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the immutable identity definition.</summary>
    public TestIdentity Build() => new()
    {
        AuthenticationType = _authenticationType,
        NameClaimType = _nameClaimType,
        RoleClaimType = _roleClaimType,
        Claims = _claims.ToArray()
    };
}
