using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/sponsors")]
public sealed class SponsorsController(UserDirectoryService service) : ControllerBase
{
    /// <summary>Sponsors a Contractor can address an initial PTW to.</summary>
    [HttpGet]
    public Task<PagedResponse<SponsorOptionResponse>> List(CancellationToken cancellationToken) =>
        service.ListSponsorOptionsAsync(cancellationToken);
}
