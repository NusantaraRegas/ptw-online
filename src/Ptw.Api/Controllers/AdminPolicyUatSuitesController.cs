using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/policy-uat-suites")]
public sealed class AdminPolicyUatSuitesController(PolicyUatService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<PolicyUatSuiteResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PolicyUatSuiteResponse>> List(CancellationToken cancellationToken) =>
        service.ListSuitesAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PolicyUatSuiteResponse>(StatusCodes.Status200OK)]
    public Task<PolicyUatSuiteResponse> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetSuiteAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<PolicyUatSuiteResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PolicyUatSuiteResponse>> Create(
        PolicyUatSuiteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateSuiteAsync(
            request,
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("{id:guid}/runs")]
    [ProducesResponseType<PagedResponse<PolicyUatRunResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PolicyUatRunResponse>> ListRuns(
        Guid id,
        CancellationToken cancellationToken) => service.ListRunsAsync(id, cancellationToken);

    [HttpPost("{id:guid}/runs")]
    [ProducesResponseType<PolicyUatRunResponse>(StatusCodes.Status200OK)]
    public Task<PolicyUatRunResponse> Run(Guid id, CancellationToken cancellationToken) =>
        service.RunSuiteAsync(
            id,
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);

    private string CorrelationId =>
        HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
