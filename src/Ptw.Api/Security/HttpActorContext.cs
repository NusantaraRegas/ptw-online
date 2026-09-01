using System.Security.Claims;
using Ptw.Application;

namespace Ptw.Api.Security;

internal sealed class HttpActorContext(IHttpContextAccessor accessor) : IActorContext
{
    public Actor Current
    {
        get
        {
            var principal = accessor.HttpContext?.User ?? throw new UnauthorizedAccessException("Konteks pengguna tidak tersedia.");
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new UnauthorizedAccessException("Identitas pengguna tidak valid.");
            return new Actor(
                id,
                principal.Identity?.Name ?? id,
                principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase),
                principal.FindAll("location_scope").Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase),
                principal.FindAll("competency").Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
    }
}
