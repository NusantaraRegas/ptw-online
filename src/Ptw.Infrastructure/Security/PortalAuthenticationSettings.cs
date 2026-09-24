using Microsoft.Extensions.Configuration;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Connection settings for credential verification through the Portal Gas Project API
/// (configuration section <c>PortalAuth</c>). The Portal API owns the Active Directory bind and the
/// portal's own local Identity fallback; this system only asks it whether a credential is valid.
/// </summary>
public sealed class PortalAuthenticationSettings
{
    public const string SectionName = "PortalAuth";
    public const string DefaultAuthenticatePath = "api/v1/User/SecureAuth";

    public bool Enabled { get; init; }
    public Uri? BaseUrl { get; init; }
    public string AuthenticatePath { get; init; } = DefaultAuthenticatePath;
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Absolute endpoint that receives the credential; null when the portal is disabled.</summary>
    public Uri? AuthenticateEndpoint =>
        BaseUrl is null ? null : new Uri(BaseUrl, AuthenticatePath.TrimStart('/'));

    public static PortalAuthenticationSettings FromConfiguration(IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(SectionName);
        var baseUrlText = section["BaseUrl"]?.Trim() ?? string.Empty;
        // Presence of a base URL turns the portal on unless Enabled says otherwise, so the minimal
        // { BaseUrl } block handed over by infrastructure works without extra keys.
        var enabled = bool.TryParse(section["Enabled"], out var configuredEnabled)
            ? configuredEnabled
            : !string.IsNullOrWhiteSpace(baseUrlText);
        if (!enabled)
        {
            return new PortalAuthenticationSettings { Enabled = false };
        }

        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "PortalAuth yang aktif memerlukan BaseUrl absolut dengan skema http atau https.");
        }

        // The credential travels in the request body, so a clear-text portal URL is only acceptable
        // on a developer workstation. Production must reach the portal over TLS.
        if (baseUrl.Scheme == Uri.UriSchemeHttp && !isDevelopment)
        {
            throw new InvalidOperationException(
                "PortalAuth:BaseUrl harus memakai https di luar environment Development.");
        }

        var path = section["AuthenticatePath"]?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultAuthenticatePath;
        }

        // Only an http(s) URL is rejected here: on Linux a leading-slash path parses as an absolute
        // file:// URI, which is still a valid relative path for Uri composition below.
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolutePath)
            && (absolutePath.Scheme == Uri.UriSchemeHttp || absolutePath.Scheme == Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("PortalAuth:AuthenticatePath harus berupa path relatif.");
        }

        return new PortalAuthenticationSettings
        {
            Enabled = true,
            // A trailing slash keeps Uri composition from dropping a base path such as /portal/.
            BaseUrl = baseUrl.AbsolutePath.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/"),
            AuthenticatePath = path,
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0 ? timeout : 10
        };
    }
}
