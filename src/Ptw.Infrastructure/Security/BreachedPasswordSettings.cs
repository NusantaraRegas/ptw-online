using Microsoft.Extensions.Configuration;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Configuration section <c>PasswordPolicy:BreachCheck</c>. The offline deny-list always applies; the
/// online k-anonymity range check (Have I Been Pwned protocol) is opt-in because it needs outbound
/// https from the API container.
/// </summary>
public sealed class BreachedPasswordSettings
{
    public const string SectionName = "PasswordPolicy:BreachCheck";
    public const string DefaultBaseUrl = "https://api.pwnedpasswords.com/range/";

    public bool OnlineEnabled { get; init; }
    public Uri BaseUrl { get; init; } = new(DefaultBaseUrl);
    public int TimeoutSeconds { get; init; } = 5;

    public static BreachedPasswordSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var enabled = bool.TryParse(section["Enabled"], out var configured) && configured;
        var baseUrlText = section["BaseUrl"]?.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(baseUrlText) ? new Uri(DefaultBaseUrl) : new Uri(baseUrlText, UriKind.Absolute);
        if (enabled && baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            // The range prefix reveals nothing on its own, but the response is trusted as a verdict,
            // so it must not be tamperable in transit.
            throw new InvalidOperationException("PasswordPolicy:BreachCheck:BaseUrl harus memakai https.");
        }

        return new BreachedPasswordSettings
        {
            OnlineEnabled = enabled,
            BaseUrl = baseUrl.AbsolutePath.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/"),
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0 ? timeout : 5
        };
    }
}
