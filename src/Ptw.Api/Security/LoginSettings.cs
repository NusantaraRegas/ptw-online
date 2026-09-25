using Ptw.Infrastructure.Security;

namespace Ptw.Api.Security;

/// <summary>
/// Decides whether <c>POST /api/v1/auth/login</c> is served (configuration section
/// <c>Authentication</c>). Development keeps the endpoint on by default; every other environment is
/// fail-closed until <c>Authentication:LoginEnabled=true</c> is set explicitly, and then only with the
/// Portal API configured, because production identity is "Portal-verified credential plus registered
/// local account" (OPN-007), never local passwords alone.
/// </summary>
public sealed class LoginSettings
{
    public const string EnabledKey = "Authentication:LoginEnabled";

    public bool Enabled { get; init; }

    public static LoginSettings FromConfiguration(
        IConfiguration configuration,
        bool isDevelopment,
        PortalAuthenticationSettings portal)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(portal);
        var enabled = bool.TryParse(configuration[EnabledKey], out var configured)
            ? configured
            : isDevelopment;
        if (enabled && !isDevelopment && !portal.Enabled)
        {
            throw new InvalidOperationException(
                "Authentication:LoginEnabled=true di luar Development memerlukan PortalAuth:BaseUrl yang aktif "
                + "(https, atau http dengan PortalAuth:AllowInsecureHttp=true).");
        }

        return new LoginSettings { Enabled = enabled };
    }
}
