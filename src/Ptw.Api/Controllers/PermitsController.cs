using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/permits")]
public sealed class PermitsController(
    PermitService service,
    PermitAttachmentService attachmentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<PermitResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PermitResponse>> List(CancellationToken cancellationToken) => service.ListAsync(cancellationToken);

    [HttpGet("/api/v1/tasks")]
    [ProducesResponseType<PagedResponse<PermitTaskResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PermitTaskResponse>> ListTasks(CancellationToken cancellationToken) =>
        service.ListTasksAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermitResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(id, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpGet("{id:guid}/attachments")]
    [ProducesResponseType<IReadOnlyList<PermitAttachmentResponse>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PermitAttachmentResponse>> ListAttachments(
        Guid id,
        CancellationToken cancellationToken) =>
        attachmentService.ListAsync(id, cancellationToken);

    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PermitAttachmentMutationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PermitAttachmentMutationResponse>> UploadAttachment(
        Guid id,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var result = await attachmentService.UploadAsync(
            id,
            file.FileName,
            file.ContentType,
            file.Length,
            content,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = result.ETag;
        return CreatedAtAction(
            nameof(DownloadAttachment),
            new { id, attachmentId = result.Attachment.Id },
            result);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/content")]
    public async Task<IActionResult> DownloadAttachment(
        Guid id,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var download = await attachmentService.DownloadAsync(id, attachmentId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(download.Content, download.MediaType, download.FileName, enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/attachments/{attachmentId:guid}/remove")]
    [ProducesResponseType<PermitAttachmentMutationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermitAttachmentMutationResponse>> RemoveAttachment(
        Guid id,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var result = await attachmentService.RemoveAsync(
            id,
            attachmentId,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = result.ETag;
        return result;
    }

    [HttpGet("{id:guid}/activity")]
    [ProducesResponseType<PagedResponse<PermitActivityResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PermitActivityResponse>> ListActivity(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        service.ListActivityAsync(id, offset, limit, cancellationToken);

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<PagedResponse<PermitVersionResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResponse<PermitVersionResponse>> ListVersions(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        service.ListVersionsAsync(id, offset, limit, cancellationToken);

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

    [HttpPost("{id:guid}/renewals")]
    [ProducesResponseType<PermitRenewalResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PermitRenewalResponse>> RequestRenewal(
        Guid id,
        RequestPermitRenewalRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.RequestRenewalAsync(
            id,
            request,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.Renewal.ETag;
        return CreatedAtAction(nameof(Get), new { id = response.Renewal.Id }, response);
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

    [HttpPost("{id:guid}/validations/hsse/endorse")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> EndorseHsseValidation(
        Guid id,
        EndorsePermitValidationRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.EndorseHsseValidationAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> Approve(
        Guid id,
        ApprovePermitRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ApproveAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/request-revision")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> RequestRevision(
        Guid id,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.RequestRevisionAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> Reject(
        Guid id,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.RejectAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/suspensions/request")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> RequestSuspension(
        Guid id,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.RequestSuspensionAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/suspensions/approve")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> ApproveSuspension(
        Guid id,
        ConfirmPermitActionRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ApproveSuspensionAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/completion/declare")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> DeclareCompletion(
        Guid id,
        ConfirmPermitActionRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.DeclareCompletionAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/completion/confirm/hsse")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> ConfirmHsseCompletion(
        Guid id,
        ConfirmPermitActionRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ConfirmHsseCompletionAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/completion/confirm/area-owner")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> ConfirmAreaOwnerCompletion(
        Guid id,
        ConfirmPermitActionRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.ConfirmAreaOwnerCompletionAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/close")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> Close(
        Guid id,
        ConfirmPermitActionRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.CloseAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    [HttpPost("{id:guid}/issue")]
    [ProducesResponseType<PermitResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PermitResponse>> Issue(
        Guid id,
        IssuePermitRequest request,
        CancellationToken cancellationToken) =>
        CommandAsync((etag, key) => service.IssueAsync(
            id,
            request,
            etag,
            key,
            CorrelationId,
            cancellationToken));

    private async Task<ActionResult<PermitResponse>> CommandAsync(
        Func<string, string, Task<PermitResponse>> command)
    {
        var response = await command(
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString());
        Response.Headers.ETag = response.ETag;
        return response;
    }

    private string CorrelationId => HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
