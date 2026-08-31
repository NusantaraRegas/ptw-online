using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ptw.Api.Security;

internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IWebHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentIdentity";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers["X-Dev-User"].FirstOrDefault() ?? "sponsor.demo";
        var displayName = Request.Headers["X-Dev-Name"].FirstOrDefault() ?? "Sponsor Demo";
        var roles = Split(Request.Headers["X-Dev-Roles"].FirstOrDefault() ?? "Sponsor,Administrator");
        var locations = Split(Request.Headers["X-Dev-Locations"].FirstOrDefault() ?? "*");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new("identity_source", "development")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(locations.Select(location => new Claim("location_scope", location)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
