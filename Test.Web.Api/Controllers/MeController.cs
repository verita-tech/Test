using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Test.Web.Api.Contracts;
using Test.Web.Api.Services;

namespace Test.Web.Api.Controllers;

/// <summary>
/// Authenticated-only (no AdAccess policy) so the Blazor app can show "signed in as X but not
/// authorized" instead of a bare 403 without context. The claims returned here are what the
/// client's AuthenticationStateProvider builds its principal from.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MeController(IAdAccessChecker checker) : ControllerBase
{
    private static readonly string[] ExposedClaimTypes = ["name", "preferred_username", "email", "groups"];

    [HttpGet]
    public ActionResult<MeResponse> Get()
    {
        var displayName = User.FindFirst("name")?.Value
                          ?? User.FindFirst("preferred_username")?.Value
                          ?? "Unbekannt";

        var claims = User.Claims
            .Where(c => ExposedClaimTypes.Contains(c.Type))
            .Select(c => new ClaimDto(c.Type, c.Value))
            .ToArray();

        return new MeResponse(displayName, checker.IsAuthorized(User), claims);
    }
}
