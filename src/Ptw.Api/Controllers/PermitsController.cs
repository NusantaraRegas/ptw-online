using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/permits")]
public sealed class PermitsController(PermitService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<PermitResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PermitResponse>> List(CancellationToken cancellationToken) => service.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermitResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(id, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PermitResponse>> Create(PermitDraftRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, CorrelationId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}/draft")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermitResponse>> UpdateDraft(
        Guid id,
        PermitDraftRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.UpdateDraftAsync(id, request, Request.Headers.IfMatch.ToString(), CorrelationId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermitResponse>> Submit(
        Guid id,
        SubmitPermitRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.SubmitAsync(
            id,
            request,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    private string CorrelationId => HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
