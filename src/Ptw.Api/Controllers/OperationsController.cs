using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/operations")]
public sealed class OperationsController(OperationsBoardService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OperationsBoardResponse>(StatusCodes.Status200OK)]
    public Task<OperationsBoardResponse> List(
        [FromQuery] string? status,
        [FromQuery] string? locationId,
        [FromQuery] string? permitClass,
        [FromQuery] string? search,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(
            status,
            locationId,
            permitClass,
            search,
            offset,
            limit,
            cancellationToken);
}
