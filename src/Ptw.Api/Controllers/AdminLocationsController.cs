using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/locations")]
public sealed class AdminLocationsController(LocationMasterService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<LocationMasterResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<LocationMasterResponse>> List(CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<LocationMasterResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LocationMasterResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(id, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost]
    [ProducesResponseType<LocationMasterResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<LocationMasterResponse>> Create(
        LocationDraftRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, CorrelationId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}/draft")]
    [ProducesResponseType<LocationMasterResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LocationMasterResponse>> UpdateDraft(
        Guid id,
        LocationDraftRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.UpdateDraftAsync(
            id,
            request,
            Request.Headers.IfMatch.ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<LocationMasterResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<LocationMasterResponse>> Submit(Guid id, CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.SubmitAsync(id, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<LocationMasterResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<LocationMasterResponse>> Approve(Guid id, CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ApproveAsync(id, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/return-for-changes")]
    [ProducesResponseType<LocationMasterResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<LocationMasterResponse>> ReturnForChanges(
        Guid id,
        ReturnLocationForChangesRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ReturnForChangesAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    private async Task<ActionResult<LocationMasterResponse>> CommandAsync(
        Func<string, string, Task<LocationMasterResponse>> command)
    {
        var response = await command(
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString());
        Response.Headers.ETag = response.ETag;
        return response;
    }

    private string CorrelationId =>
        HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
