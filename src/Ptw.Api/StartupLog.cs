using Ptw.Infrastructure.Security;

namespace Ptw.Api;

/// <summary>
/// Startup-time operator warnings that must be visible in the container log every boot, so an
/// accepted-risk configuration is never silently carried over to a host where it was not accepted.
/// </summary>
internal static class StartupLog
{
    private static readonly Action<ILogger, string, Exception?> InsecurePortalHttpAccepted =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1100, "InsecurePortalHttpAccepted"),
            "PortalAuth:AllowInsecureHttp=true: kredensial login diteruskan ke Portal API tanpa TLS ({Endpoint}). "
            + "Risiko diterima melalui amendemen OPN-007; hanya untuk portal di jaringan internal.");

    public static void WarnOnAcceptedRisks(ILogger logger, PortalAuthenticationSettings portal)
    {
        if (portal.InsecureHttpAccepted && portal.AuthenticateEndpoint is not null)
        {
            InsecurePortalHttpAccepted(logger, portal.AuthenticateEndpoint.AbsoluteUri, null);
        }
    }
}
