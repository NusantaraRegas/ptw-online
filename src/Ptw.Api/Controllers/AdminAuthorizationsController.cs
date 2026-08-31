using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/authorizations")]
public sealed class AdminAuthorizationsController(UserAuthorizationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<UserAuthorizationResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<UserAuthorizationResponse>> List(CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserAuthorizationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserAuthorizationResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(id, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost]
    [ProducesResponseType<UserAuthorizationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserAuthorizationResponse>> Create(
        UserAuthorizationDraftRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, CorrelationId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}/draft")]
    [ProducesResponseType<UserAuthorizationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserAuthorizationResponse>> UpdateDraft(
        Guid id,
        UserAuthorizationDraftRequest request,
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
    [ProducesResponseType<UserAuthorizationResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAuthorizationResponse>> Submit(Guid id, CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.SubmitAsync(id, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<UserAuthorizationResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAuthorizationResponse>> Approve(Guid id, CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ApproveAsync(id, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/return-for-changes")]
    [ProducesResponseType<UserAuthorizationResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAuthorizationResponse>> ReturnForChanges(
        Guid id,
        ReturnAuthorizationForChangesRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ReturnForChangesAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    private async Task<ActionResult<UserAuthorizationResponse>> CommandAsync(
        Func<string, string, Task<UserAuthorizationResponse>> command)
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
