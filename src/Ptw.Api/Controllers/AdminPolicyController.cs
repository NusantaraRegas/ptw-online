using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/policy-readiness")]
public sealed class AdminPolicyController(OperationalPolicyService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OperationalPolicyReadinessResponse>(StatusCodes.Status200OK)]
    public Task<OperationalPolicyReadinessResponse> Get(CancellationToken cancellationToken) =>
        service.GetReadinessAsync(cancellationToken);
}
