using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/user-guide")]
public sealed class UserGuideController(UserGuideService service) : ControllerBase
{
    [HttpGet]
    public Task<UserGuideResponse> Get(CancellationToken cancellationToken) =>
        service.GetAsync(cancellationToken);

    [HttpGet("content")]
    public async Task<IActionResult> Download(CancellationToken cancellationToken)
    {
        var download = await service.DownloadAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(download.Content, download.MediaType, download.FileName, enableRangeProcessing: false);
    }
}
