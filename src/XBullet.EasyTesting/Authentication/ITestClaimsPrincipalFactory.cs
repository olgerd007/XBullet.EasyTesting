using System.Security.Claims;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Materializes a server-side claims principal from a transported test-user definition.</summary>
public interface ITestClaimsPrincipalFactory
{
    /// <summary>Creates the claims principal used by the test authentication ticket.</summary>
    /// <param name="user">
    /// The transported test-user definition to materialize. The factory reads but does not own or
    /// mutate the definition.
    /// </param>
    /// <returns>
    /// A claims principal representing all identities and claims in <paramref name="user"/>.
    /// The caller owns the returned principal.
    /// </returns>
    ClaimsPrincipal CreatePrincipal(TestUser user);
}
