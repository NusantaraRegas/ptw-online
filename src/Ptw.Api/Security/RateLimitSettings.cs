using System.Security.Claims;

namespace Ptw.Api.Security;

/// <summary>
/// In-process rate-limit windows (configuration section <c>RateLimiting</c>). The reverse proxy is
/// the first line of defence; these values only bound what a single client can do once a request
/// has reached Kestrel.
/// </summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";
    public const string LoginPolicy = "login";

    public int GlobalPermitLimit { get; init; } = 120;
    public int LoginPermitLimit { get; init; } = 10;
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Signed-in callers share a bucket per subject id; anonymous callers per client address.</summary>
    public static string ClientKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subjectId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(subjectId) ? AddressKey(context) : $"subject:{subjectId}";
    }

    public static string AddressKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return $"address:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
