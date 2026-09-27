namespace XBullet.EasyTesting.Authentication;

/// <summary>Fluently constructs an additional identity for a simulated principal.</summary>
/// <remarks>This mutable builder is not thread-safe. Each call to <see cref="Build"/> returns a snapshot.</remarks>
public sealed class TestIdentityBuilder
{
    private readonly List<TestClaim> _claims = [];
    private string _authenticationType = TestAuthenticationDefaults.AuthenticationScheme;
    private string _nameClaimType = System.Security.Claims.ClaimTypes.Name;
    private string _roleClaimType = System.Security.Claims.ClaimTypes.Role;

    /// <summary>Sets the identity authentication type.</summary>
    /// <param name="authenticationType">The non-empty authentication type exposed by the identity.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestIdentityBuilder WithAuthenticationType(string authenticationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        _authenticationType = authenticationType;
        return this;
    }

    /// <summary>Sets the claim types used for name and role resolution.</summary>
    /// <param name="nameClaimType">
    /// The non-empty claim type resolved through <see cref="System.Security.Principal.IIdentity.Name"/>.
    /// </param>
    /// <param name="roleClaimType">The non-empty claim type used by role checks.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestIdentityBuilder WithClaimTypes(string nameClaimType, string roleClaimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleClaimType);
        _nameClaimType = nameClaimType;
        _roleClaimType = roleClaimType;
        return this;
    }

    /// <summary>Adds a claim to this identity.</summary>
    /// <param name="type">The non-empty claim-type identifier.</param>
    /// <param name="value">The non-null claim value; an empty value is accepted.</param>
    /// <returns>This builder so additional identity values can be configured.</returns>
    public TestIdentityBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new TestClaim(type, value));
        return this;
    }

    /// <summary>Creates the immutable identity definition.</summary>
    /// <returns>
    /// A new identity containing a snapshot of the current claim-type settings and claims.
    /// </returns>
    public TestIdentity Build() => new()
    {
        AuthenticationType = _authenticationType,
        NameClaimType = _nameClaimType,
        RoleClaimType = _roleClaimType,
        Claims = _claims.ToArray()
    };
}
