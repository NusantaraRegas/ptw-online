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
    public Task<PagedResponse<PermitResponse>> List(CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken);

    [HttpGet("/api/v1/tasks")]
    public Task<PagedResponse<PermitTaskResponse>> ListTasks(CancellationToken cancellationToken) =>
        service.ListTasksAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PermitResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(id, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpGet("{id:guid}/attachments")]
    public Task<IReadOnlyList<PermitAttachmentResponse>> ListAttachments(
        Guid id,
        CancellationToken cancellationToken) =>
        attachmentService.ListAsync(id, cancellationToken);

    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<PermitAttachmentMutationResponse>> UploadAttachment(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] string? category,
        [FromForm] string? supportingDocumentCode,
        [FromForm] string? documentNumber,
        [FromForm] string? documentRevision,
        [FromForm] DateTimeOffset? documentDate,
        [FromForm] Guid? printPackageId,
        [FromForm] Guid? supersedesAttachmentId,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var result = await attachmentService.UploadAsync(
            id,
            file.FileName,
            file.ContentType,
            file.Length,
            content,
            category ?? "SUPPORTING",
            supportingDocumentCode,
            documentNumber,
            documentRevision,
            documentDate,
            printPackageId,
            supersedesAttachmentId,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = result.ETag;
        return CreatedAtAction(nameof(DownloadAttachment), new { id, attachmentId = result.Attachment.Id }, result);
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
    public Task<PagedResponse<PermitActivityResponse>> ListActivity(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        service.ListActivityAsync(id, offset, limit, cancellationToken);

    [HttpGet("{id:guid}/versions")]
    public Task<PagedResponse<PermitVersionResponse>> ListVersions(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        service.ListVersionsAsync(id, offset, limit, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<PermitResponse>> Create(
        PermitDraftRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, CorrelationId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}/draft")]
    public Task<ActionResult<PermitResponse>> UpdateDraft(
        Guid id,
        PermitDraftRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, _) => service.UpdateDraftAsync(id, request, etag, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/renew")]
    public Task<ActionResult<PermitResponse>> RequestRenewal(
        Guid id,
        RequestPermitRenewalRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RequestRenewalAsync(
            id, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/renewal-tasks/{taskId:guid}/request-evidence")]
    public Task<ActionResult<PermitResponse>> RequestRenewalEvidenceReplacement(
        Guid taskId,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RequestRenewalEvidenceReplacementAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/renewal-tasks/{taskId:guid}/reject")]
    public Task<ActionResult<PermitResponse>> RejectRenewal(
        Guid taskId,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RejectRenewalAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/renewal-tasks/{taskId:guid}/approve")]
    public async Task<ActionResult<PermitRenewalResponse>> ApproveRenewal(
        Guid taskId,
        ApproveRenewalRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.ApproveRenewalAsync(
            taskId,
            request,
            Request.Headers.IfMatch.ToString(),
            Request.Headers["Idempotency-Key"].ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.SourceETag;
        return CreatedAtAction(nameof(Get), new { id = response.Renewal.Id }, response);
    }

    [HttpPost("{id:guid}/submit")]
    public Task<ActionResult<PermitResponse>> Submit(
        Guid id,
        SubmitPermitRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.SubmitAsync(id, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/tasks/{taskId:guid}/validate")]
    public Task<ActionResult<PermitResponse>> Validate(
        Guid taskId,
        ValidateSubmissionRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.ValidateSubmissionAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/tasks/{taskId:guid}/escalate")]
    public Task<ActionResult<PermitResponse>> EscalateValidation(
        Guid taskId,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.EscalateValidationAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/tasks/{taskId:guid}/revision")]
    public Task<ActionResult<PermitResponse>> RequestRevision(
        Guid taskId,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RequestRevisionAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/tasks/{taskId:guid}/reject")]
    public Task<ActionResult<PermitResponse>> Reject(
        Guid taskId,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RejectAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/tasks/{taskId:guid}/approve-and-issue")]
    public Task<ActionResult<PermitResponse>> ApproveAndIssue(
        Guid taskId,
        ApproveAndIssuePermitRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.ApproveAndIssueAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/suspensions")]
    public Task<ActionResult<PermitResponse>> Suspend(
        Guid id,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.SuspendAsync(
            id, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/suspensions/resolve")]
    public Task<ActionResult<PermitResponse>> ResolveSuspension(
        Guid id,
        ResolveSuspensionRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.ResolveSuspensionAsync(
            id, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/closure-requests")]
    public Task<ActionResult<PermitResponse>> RequestClosure(
        Guid id,
        RequestClosureRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RequestClosureAsync(
            id, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/closure-tasks/{taskId:guid}/request-evidence")]
    public Task<ActionResult<PermitResponse>> RequestClosureEvidenceReplacement(
        Guid taskId,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.RequestClosureEvidenceReplacementAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("/api/v1/closure-tasks/{taskId:guid}/close")]
    public Task<ActionResult<PermitResponse>> Close(
        Guid taskId,
        ClosePermitRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.CloseAsync(
            taskId, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<PermitResponse>> Cancel(
        Guid id,
        PermitReasonRequest request,
        CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.CancelAsync(
            id, request, etag, key, CorrelationId, cancellationToken));

    [HttpPost("{id:guid}/expire")]
    public Task<ActionResult<PermitResponse>> Expire(Guid id, CancellationToken cancellationToken) =>
        PermitCommandAsync((etag, key) => service.ExpireAsync(id, etag, key, CorrelationId, cancellationToken));

    private async Task<ActionResult<PermitResponse>> PermitCommandAsync(
        Func<string, string, Task<PermitResponse>> command)
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
