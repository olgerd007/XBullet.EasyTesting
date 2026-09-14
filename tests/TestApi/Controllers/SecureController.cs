using Microsoft.AspNetCore.Authorization;
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
}
