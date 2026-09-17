using System.Security.Claims;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Materializes a server-side claims principal from a transported test-user definition.</summary>
public interface ITestClaimsPrincipalFactory
{
    /// <summary>Creates the claims principal used by the test authentication ticket.</summary>
    ClaimsPrincipal CreatePrincipal(TestUser user);
}
