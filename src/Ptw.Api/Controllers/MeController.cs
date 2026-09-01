using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
public sealed class MeController : ControllerBase
{
    [HttpGet]
    public MeResponse Get() => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
        User.Identity?.Name ?? "Unknown",
        User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray(),
        User.FindAll("location_scope").Select(x => x.Value).ToArray(),
        User.FindAll("competency").Select(x => x.Value).ToArray(),
        User.HasClaim("identity_source", "development"));
}
