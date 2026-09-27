using System.Security.Claims;

namespace XBullet.EasyTesting.Authentication;

/// <summary>
/// Creates standard claims identities for a transported test user and provides overridable identity
/// materialization for applications with custom <see cref="ClaimsIdentity"/> types.
/// </summary>
public class TestClaimsPrincipalFactory : ITestClaimsPrincipalFactory
{
    /// <inheritdoc />
    public virtual ClaimsPrincipal CreatePrincipal(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identities = new List<ClaimsIdentity> { CreatePrimaryIdentity(user) };
        identities.AddRange(user.AdditionalIdentities.Select(CreateAdditionalIdentity));
        return new ClaimsPrincipal(identities);
    }

    /// <summary>Creates the primary identity represented by the test user.</summary>
    /// <param name="user">
    /// The non-null transported definition whose name identifier, name, roles, claims,
    /// authentication type, and claim-type mappings are copied. The factory does not retain, own,
    /// or mutate it.
    /// </param>
    /// <returns>
    /// A new claims identity containing the primary user claims. Ownership passes to the caller,
    /// which normally adds it to the returned principal.
    /// </returns>
    protected virtual ClaimsIdentity CreatePrimaryIdentity(TestUser user) =>
        new(
            CreatePrimaryClaims(user),
            user.AuthenticationType,
            ClaimTypes.Name,
            ClaimTypes.Role);

    /// <summary>
    /// Creates an additional identity. Override this to materialize application-specific identity
    /// classes such as an application-specific <c>ApiUserIdentity</c>.
    /// </summary>
    /// <param name="identity">
    /// The non-null additional-identity definition whose claims, authentication type, name-claim
    /// type, and role-claim type are copied. The factory does not retain, own, or mutate it.
    /// </param>
    /// <returns>
    /// A new claims identity containing the supplied definition. Ownership passes to the caller,
    /// which normally adds it to the returned principal.
    /// </returns>
    protected virtual ClaimsIdentity CreateAdditionalIdentity(TestIdentity identity) =>
        new(
            identity.Claims.Select(claim => new Claim(claim.Type, claim.Value)),
            identity.AuthenticationType,
            identity.NameClaimType,
            identity.RoleClaimType);

    private static IEnumerable<Claim> CreatePrimaryClaims(TestUser user)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, user.NameIdentifier);
        yield return new Claim(ClaimTypes.Name, user.Name);

        foreach (var role in user.Roles)
        {
            yield return new Claim(ClaimTypes.Role, role);
        }

        foreach (var claim in user.Claims)
        {
            yield return new Claim(claim.Type, claim.Value);
        }
    }
}
