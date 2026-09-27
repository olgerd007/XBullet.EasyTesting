using System.Security.Claims;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Describes the identity attached to one integration-test request.</summary>
public sealed record TestUser
{
    /// <summary>Gets the application authentication scheme this identity targets.</summary>
    /// <value>
    /// The scheme selected by the simulated authentication handler. The default is
    /// <see cref="TestAuthenticationDefaults.AuthenticationScheme"/>.
    /// </value>
    public string AuthenticationScheme { get; init; } = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Gets the authentication type exposed by the resulting claims identity.</summary>
    /// <value>
    /// The value returned by <see cref="System.Security.Principal.IIdentity.AuthenticationType"/>.
    /// The default is <see cref="TestAuthenticationDefaults.AuthenticationScheme"/>.
    /// </value>
    public string AuthenticationType { get; init; } = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Gets the value exposed through <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    /// <value>A per-instance GUID in <c>N</c> format by default.</value>
    public string NameIdentifier { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets the authenticated user's display name.</summary>
    /// <value><c>integration-test-user</c> by default.</value>
    public string Name { get; init; } = "integration-test-user";

    /// <summary>Gets the roles used by role-based authorization.</summary>
    /// <value>
    /// The role values converted to <see cref="ClaimTypes.Role"/> claims. The default is an empty
    /// collection.
    /// </value>
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>Gets additional claims used by policy-based authorization.</summary>
    /// <value>
    /// Claims appended after the built-in name, name-identifier, and role claims. The default is an
    /// empty collection.
    /// </value>
    public IReadOnlyCollection<TestClaim> Claims { get; init; } = Array.Empty<TestClaim>();

    /// <summary>Gets additional identities attached to the resulting principal.</summary>
    /// <value>
    /// Additional claims identities appended after the primary identity. The default is an empty
    /// collection.
    /// </value>
    public IReadOnlyCollection<TestIdentity> AdditionalIdentities { get; init; } = Array.Empty<TestIdentity>();

    /// <summary>Gets authentication-ticket properties exposed by the simulated handler.</summary>
    /// <value>
    /// Ticket-property values keyed with ordinal comparison. Values may be <see langword="null"/>.
    /// The default dictionary is empty.
    /// </value>
    public IReadOnlyDictionary<string, string?> AuthenticationProperties { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>Creates a test user with optional roles and claims.</summary>
    /// <param name="name">
    /// The display-name claim value. When omitted, <c>integration-test-user</c> is used. This method
    /// does not normalize or validate the value.
    /// </param>
    /// <param name="nameIdentifier">
    /// The name-identifier claim value. When <see langword="null"/>, a new GUID in <c>N</c> format is
    /// generated.
    /// </param>
    /// <param name="roles">
    /// Role values to copy into the user. When <see langword="null"/>, no roles are added. The
    /// returned user owns the copied collection.
    /// </param>
    /// <param name="claims">
    /// Additional claims to copy into the user. When <see langword="null"/>, no additional claims
    /// are added. The returned user owns the copied collection.
    /// </param>
    /// <param name="authenticationScheme">
    /// The target application scheme. When omitted, the default simulated scheme is used. This
    /// method does not validate that the scheme is registered.
    /// </param>
    /// <param name="authenticationType">
    /// The authentication type exposed by the resulting identity. When omitted, the default
    /// simulated scheme name is used.
    /// </param>
    /// <returns>A new immutable test-user definition containing snapshots of the supplied values.</returns>
    public static TestUser Create(
        string name = "integration-test-user",
        string? nameIdentifier = null,
        IEnumerable<string>? roles = null,
        IEnumerable<TestClaim>? claims = null,
        string authenticationScheme = TestAuthenticationDefaults.AuthenticationScheme,
        string authenticationType = TestAuthenticationDefaults.AuthenticationScheme) =>
        new()
        {
            AuthenticationScheme = authenticationScheme,
            AuthenticationType = authenticationType,
            Name = name,
            NameIdentifier = nameIdentifier ?? Guid.NewGuid().ToString("N"),
            Roles = roles?.ToArray() ?? Array.Empty<string>(),
            Claims = claims?.ToArray() ?? Array.Empty<TestClaim>()
        };

    /// <summary>Starts a fluent test-user definition.</summary>
    /// <returns>A new mutable builder initialized with the default simulated identity values.</returns>
    public static TestUserBuilder CreateBuilder() => new();

    internal IEnumerable<Claim> ToClaims()
    {
        yield return new Claim(ClaimTypes.NameIdentifier, NameIdentifier);
        yield return new Claim(ClaimTypes.Name, Name);

        foreach (var role in Roles)
        {
            yield return new Claim(ClaimTypes.Role, role);
        }

        foreach (var claim in Claims)
        {
            yield return new Claim(claim.Type, claim.Value);
        }
    }
}

/// <summary>A serializable claim used to construct a <see cref="TestUser"/>.</summary>
/// <param name="Type">The non-null claim-type identifier.</param>
/// <param name="Value">The non-null claim value.</param>
public sealed record TestClaim(string Type, string Value);

/// <summary>Describes an additional identity attached to a simulated test principal.</summary>
public sealed record TestIdentity
{
    /// <summary>Gets the identity authentication type.</summary>
    /// <value>
    /// The authentication type exposed by the identity. The default is the simulated scheme name.
    /// </value>
    public string AuthenticationType { get; init; } = TestAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Gets the claim type used for <see cref="System.Security.Principal.IIdentity.Name"/>.</summary>
    /// <value><see cref="ClaimTypes.Name"/> by default.</value>
    public string NameClaimType { get; init; } = ClaimTypes.Name;

    /// <summary>Gets the claim type used for role checks.</summary>
    /// <value><see cref="ClaimTypes.Role"/> by default.</value>
    public string RoleClaimType { get; init; } = ClaimTypes.Role;

    /// <summary>Gets claims belonging to this identity.</summary>
    /// <value>The claims materialized into the additional identity. The default is an empty collection.</value>
    public IReadOnlyCollection<TestClaim> Claims { get; init; } = Array.Empty<TestClaim>();

    internal ClaimsIdentity ToClaimsIdentity() =>
        new(
            Claims.Select(claim => new Claim(claim.Type, claim.Value)),
            AuthenticationType,
            NameClaimType,
            RoleClaimType);
}
