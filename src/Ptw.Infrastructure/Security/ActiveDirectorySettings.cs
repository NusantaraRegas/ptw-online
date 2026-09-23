using Microsoft.Extensions.Configuration;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Connection settings for the corporate Active Directory (configuration section <c>AD</c>).
/// </summary>
public sealed class ActiveDirectorySettings
{
    public const string SectionName = "AD";

    public bool Enabled { get; init; }
    public string Server { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public int Port { get; init; } = 389;

    /// <summary>
    /// Upgrades the plain LDAP connection with StartTLS before the bind so the password never
    /// crosses the network in clear text. Off by default because the target domain controller may
    /// not offer it; the authenticator logs whenever a clear-text bind is used.
    /// </summary>
    public bool UseStartTls { get; init; }

    /// <summary>Connects with LDAPS (typically port 636) instead of plain LDAP.</summary>
    public bool UseSsl { get; init; }

    public int TimeoutSeconds { get; init; } = 10;

    public static ActiveDirectorySettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var server = section["Server"]?.Trim() ?? string.Empty;
        // Presence of a server address turns the directory on unless Enabled says otherwise, so the
        // minimal { Server, Domain, Port } block handed over by infrastructure works without extra keys.
        var enabled = bool.TryParse(section["Enabled"], out var configuredEnabled)
            ? configuredEnabled
            : !string.IsNullOrWhiteSpace(server);
        var settings = new ActiveDirectorySettings
        {
            Enabled = enabled,
            Server = server,
            Domain = section["Domain"]?.Trim() ?? string.Empty,
            Port = int.TryParse(section["Port"], out var port) ? port : 389,
            UseStartTls = bool.TryParse(section["UseStartTls"], out var startTls) && startTls,
            UseSsl = bool.TryParse(section["UseSsl"], out var ssl) && ssl,
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0 ? timeout : 10
        };
        if (!settings.Enabled)
        {
            return settings;
        }

        if (string.IsNullOrWhiteSpace(settings.Server) || string.IsNullOrWhiteSpace(settings.Domain))
        {
            throw new InvalidOperationException("AD yang aktif memerlukan Server dan Domain yang valid.");
        }

        if (settings.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("AD:Port harus berada pada rentang 1-65535.");
        }

        if (settings.UseStartTls && settings.UseSsl)
        {
            throw new InvalidOperationException("AD:UseStartTls dan AD:UseSsl tidak dapat aktif bersamaan.");
        }

        return settings;
    }
}
