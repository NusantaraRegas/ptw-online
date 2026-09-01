using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/policy-simulations")]
public sealed class AdminPolicySimulationsController(PolicySimulationService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PolicySimulationResponse>(StatusCodes.Status200OK)]
    public Task<PolicySimulationResponse> Simulate(
        PolicySimulationRequest request,
        CancellationToken cancellationToken) =>
        service.SimulateAsync(request, cancellationToken);
}
