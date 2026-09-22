using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/settings")]
public sealed class AdminSettingsController(
    DemoModeService service,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("demo-mode")]
    public Task<DemoModeResponse> GetDemoMode(CancellationToken cancellationToken) =>
        service.GetAdminAsync(cancellationToken);

    [HttpPost("demo-mode/enable")]
    public Task<DemoModeResponse> EnableDemoMode(CancellationToken cancellationToken) =>
        SetDemoMode(true, cancellationToken);

    [HttpPost("demo-mode/disable")]
    public Task<DemoModeResponse> DisableDemoMode(CancellationToken cancellationToken) =>
        SetDemoMode(false, cancellationToken);

    private Task<DemoModeResponse> SetDemoMode(bool enabled, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidRequestException(
                "demo_mode.unavailable",
                "Mode demo tidak tersedia pada lingkungan ini.");
        }

        return service.SetAsync(
            enabled,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
    }

    private string CorrelationId =>
        HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
