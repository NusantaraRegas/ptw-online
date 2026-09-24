using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ptw.Api.Security;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthenticationController(
    UserAuthenticationService authenticationService,
    DemoModeService demoModeService,
    LoginSettings loginSettings,
    IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("options")]
    public async Task<ActionResult<AuthenticationOptionsResponse>> Options(CancellationToken cancellationToken)
    {
        var response = await demoModeService.GetPublicAsync(cancellationToken);
        return new AuthenticationOptionsResponse(environment.IsDevelopment() && response.Enabled);
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.LoginPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (!loginSettings.Enabled)
        {
            throw new InvalidRequestException(
                "authentication.local_login_disabled",
                "Login tidak tersedia pada lingkungan ini (Authentication:LoginEnabled belum diaktifkan).");
        }

        var authenticated = await authenticationService.AuthenticateAsync(
            request.UserName,
            request.Password,
            cancellationToken);
        var account = authenticated.Account.Account;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.SubjectId),
            new Claim(ClaimTypes.Name, account.DisplayName),
            new Claim("identity_source", authenticated.IdentitySource)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
