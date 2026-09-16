using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/permits/{id:guid}/print-packages")]
public sealed class PrintPackagesController(PrintPackageService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<PrintPackageResponse>> List(Guid id, CancellationToken cancellationToken) =>
        service.ListAsync(id, cancellationToken);

    /// <summary>
    /// Draft preview. Always watermarked and never persisted, so it cannot be mistaken for, or used as,
    /// the official document.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var download = await service.PreviewAsync(id, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(download.Content, download.MediaType, download.FileName);
    }

    [HttpGet("{printPackageId:guid}/content")]
    public async Task<IActionResult> Download(
        Guid id,
        Guid printPackageId,
        CancellationToken cancellationToken)
    {
        var download = await service.DownloadAsync(id, printPackageId, CorrelationId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(download.Content, download.MediaType, download.FileName, enableRangeProcessing: true);
    }

    [HttpPost("{printPackageId:guid}/retry")]
    public Task<PrintPackageResponse> Retry(
        Guid id,
        Guid printPackageId,
        CancellationToken cancellationToken) =>
        service.RetryAsync(id, printPackageId, cancellationToken);

    private string CorrelationId =>
        HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
