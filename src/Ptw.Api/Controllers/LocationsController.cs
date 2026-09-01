using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
public sealed class LocationsController(LocationLookupService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<LocationOptionResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<LocationOptionResponse>> List(CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken);
}
