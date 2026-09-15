using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace TestApi.Controllers;

[ApiController]
[Route("api/secure")]
public sealed class SecureController : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        User.Identity!.Name,
        Subject = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value
    });

    [Authorize(Roles = "Administrator")]
    [HttpGet("admin")]
    public IActionResult Admin() => NoContent();

    [Authorize(Policy = "CanReadReports")]
    [HttpGet("reports")]
    public IActionResult Reports() => NoContent();

    [Authorize(Policy = "AzureAdOnly")]
    [HttpGet("azure-ad")]
    public IActionResult AzureAd() => NoContent();

    [Authorize(Policy = "ApiKeyOnly")]
    [HttpGet("api-key")]
    public IActionResult ApiKey() => NoContent();

    [Authorize(Policy = "PartnerOnly")]
    [HttpGet("partner")]
    public IActionResult Partner() => NoContent();

    [Authorize(Policy = "CertificateOnly")]
    [HttpGet("certificate")]
    public IActionResult Certificate() => NoContent();

    [Authorize]
    [HttpGet("authentication-details")]
    public async Task<IActionResult> AuthenticationDetails()
    {
        var authentication = await HttpContext.AuthenticateAsync();
        return Ok(new
        {
            Identities = User.Identities.Select(identity => new
            {
                identity.AuthenticationType,
                identity.Name,
                Claims = identity.Claims.Select(claim => new { claim.Type, claim.Value })
            }),
            Properties = authentication.Properties?.Items
        });
    }
}
