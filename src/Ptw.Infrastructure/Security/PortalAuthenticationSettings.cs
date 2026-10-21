using Microsoft.Extensions.Configuration;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Connection settings for credential verification through the Portal Gas Project API
/// (configuration section <c>PortalAuth</c>). PTW first obtains a bearer token with the configured
/// service credential, then calls the bearer-protected AD authentication endpoint for the user.
/// </summary>
public sealed class PortalAuthenticationSettings
{
    public const string SectionName = "PortalAuth";
    public const string DefaultAuthenticatePath = "api/v1/User/SecureAuth";
    public const string DefaultDirectoryAuthenticatePath = "api/v1/User/ADAuth";

    public bool Enabled { get; init; }
    public Uri? BaseUrl { get; init; }
    public string AuthenticatePath { get; init; } = DefaultAuthenticatePath;
    public string DirectoryAuthenticatePath { get; init; } = DefaultDirectoryAuthenticatePath;
    public string ServiceUserName { get; init; } = string.Empty;
    public string ServicePassword { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// True when a clear-text <c>http://</c> portal URL was accepted outside Development through the
    /// explicit <c>PortalAuth:AllowInsecureHttp</c> opt-in (OPN-007 amendment, accepted risk).
    /// </summary>
    public bool InsecureHttpAccepted { get; init; }

    /// <summary>Absolute endpoint that receives the service credential; null when disabled.</summary>
    public Uri? AuthenticateEndpoint =>
        BaseUrl is null ? null : new Uri(BaseUrl, AuthenticatePath.TrimStart('/'));

    /// <summary>Bearer-protected endpoint that validates an employee credential against AD.</summary>
    public Uri? DirectoryAuthenticateEndpoint =>
        BaseUrl is null ? null : new Uri(BaseUrl, DirectoryAuthenticatePath.TrimStart('/'));

    public static PortalAuthenticationSettings FromConfiguration(IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(SectionName);
        var baseUrlText = section["BaseUrl"]?.Trim() ?? string.Empty;
        // Presence of a base URL turns the portal on unless Enabled says otherwise. An enabled
        // configuration is fail-closed unless the separate service credential is also present.
        var enabled = bool.TryParse(section["Enabled"], out var configuredEnabled)
            ? configuredEnabled
            : !string.IsNullOrWhiteSpace(baseUrlText);
        if (!enabled)
        {
            return new PortalAuthenticationSettings { Enabled = false };
        }

        var serviceUserName = section["ServiceUserName"]?.Trim() ?? string.Empty;
        var servicePassword = section["ServicePassword"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(serviceUserName) || string.IsNullOrEmpty(servicePassword))
        {
            throw new InvalidOperationException(
                "PortalAuth yang aktif memerlukan ServiceUserName dan ServicePassword.");
        }

        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "PortalAuth yang aktif memerlukan BaseUrl absolut dengan skema http atau https.");
        }

        // Both the service and employee credentials traverse this connection, so clear text is only acceptable
        // on a developer workstation, or on a production host whose operator has explicitly accepted
        // the risk for an internal-network portal (OPN-007 amendment). The opt-in must be a literal
        // `true`: an absent or malformed value keeps the fail-closed default.
        var insecureHttpAccepted = false;
        if (baseUrl.Scheme == Uri.UriSchemeHttp && !isDevelopment)
        {
            var allowInsecureHttp = bool.TryParse(section["AllowInsecureHttp"], out var configuredAllow)
                && configuredAllow;
            if (!allowInsecureHttp)
            {
                throw new InvalidOperationException(
                    "PortalAuth:BaseUrl harus memakai https di luar environment Development, "
                    + "kecuali PortalAuth:AllowInsecureHttp=true ditetapkan secara eksplisit sebagai risiko yang diterima.");
            }

            insecureHttpAccepted = true;
        }

        var path = RelativePath(section["AuthenticatePath"], DefaultAuthenticatePath, "AuthenticatePath");
        var directoryPath = RelativePath(
            section["DirectoryAuthenticatePath"],
            DefaultDirectoryAuthenticatePath,
            "DirectoryAuthenticatePath");

        return new PortalAuthenticationSettings
        {
            Enabled = true,
            // A trailing slash keeps Uri composition from dropping a base path such as /portal/.
            BaseUrl = baseUrl.AbsolutePath.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/"),
            AuthenticatePath = path,
            DirectoryAuthenticatePath = directoryPath,
            ServiceUserName = serviceUserName,
            ServicePassword = servicePassword,
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0 ? timeout : 10,
            InsecureHttpAccepted = insecureHttpAccepted
        };
    }

    private static string RelativePath(string? configuredPath, string defaultPath, string settingName)
    {
        var path = configuredPath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = defaultPath;
        }

        // Only an http(s) URL is rejected here: on Linux a leading-slash path parses as an absolute
        // file:// URI, which is still a valid relative path for Uri composition below.
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolutePath)
            && (absolutePath.Scheme == Uri.UriSchemeHttp || absolutePath.Scheme == Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"PortalAuth:{settingName} harus berupa path relatif.");
        }

        return path;
    }
}
