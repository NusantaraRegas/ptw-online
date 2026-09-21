using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthenticationController(
    UserAuthenticationService authenticationService,
    IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidRequestException(
                "authentication.local_login_disabled",
                "Login lokal hanya tersedia pada lingkungan Development sampai kontrak IdP disahkan.");
        }

        var stored = await authenticationService.AuthenticateAsync(
            request.UserName,
            request.Password,
            cancellationToken);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, stored.Account.SubjectId),
            new Claim(ClaimTypes.Name, stored.Account.DisplayName),
            new Claim("identity_source", "development-local")
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
