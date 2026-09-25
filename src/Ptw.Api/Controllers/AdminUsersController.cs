using Microsoft.AspNetCore.Mvc;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(UserDirectoryService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<UserAccountResponse>> List(CancellationToken cancellationToken) =>
        service.ListAsync(cancellationToken);

    /// <summary>Recent login journal, newest first; Administrators use it to see lockouts and unexpected paths.</summary>
    [HttpGet("login-events")]
    public Task<PagedResponse<LoginAuditEventResponse>> LoginEvents(
        [FromQuery] int? limit,
        [FromQuery] string? subjectId,
        CancellationToken cancellationToken) =>
        service.ListLoginEventsAsync(limit, subjectId, cancellationToken);

    [HttpGet("{subjectId}")]
    public async Task<ActionResult<UserAccountResponse>> Get(
        string subjectId,
        CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(subjectId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost]
    public async Task<ActionResult<UserAccountResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, CorrelationId, cancellationToken);
        Response.Headers.ETag = response.ETag;
        return CreatedAtAction(nameof(Get), new { subjectId = response.SubjectId }, response);
    }

    [HttpPatch("{subjectId}")]
    public async Task<ActionResult<UserAccountResponse>> Update(
        string subjectId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.UpdateAsync(
            subjectId,
            request,
            Request.Headers.IfMatch.ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost("{subjectId}/active")]
    public async Task<ActionResult<UserAccountResponse>> SetActive(
        string subjectId,
        SetUserActiveRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.SetActiveAsync(
            subjectId,
            request,
            Request.Headers.IfMatch.ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost("{subjectId}/password")]
    public async Task<ActionResult<UserAccountResponse>> ResetPassword(
        string subjectId,
        ResetUserPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.ResetPasswordAsync(
            subjectId,
            request,
            Request.Headers.IfMatch.ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    /// <summary>Ends every live session of the account ("log out everywhere").</summary>
    [HttpPost("{subjectId}/sessions/revoke")]
    public async Task<ActionResult<UserAccountResponse>> RevokeSessions(
        string subjectId,
        CancellationToken cancellationToken)
    {
        var response = await service.RevokeSessionsAsync(
            subjectId,
            Request.Headers.IfMatch.ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpPost("{subjectId}/signature")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UserAccountResponse>> UploadSignature(
        string subjectId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var response = await service.UploadSignatureAsync(
            subjectId,
            file.ContentType,
            file.Length,
            content,
            Request.Headers.IfMatch.ToString(),
            CorrelationId,
            cancellationToken);
        Response.Headers.ETag = response.ETag;
        return response;
    }

    [HttpGet("{subjectId}/signature")]
    public async Task<IActionResult> GetSignature(
        string subjectId,
        CancellationToken cancellationToken)
    {
        var signature = await service.GetActiveSignatureAsync(subjectId, cancellationToken);
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        return File(signature.Content, signature.MediaType);
    }

    private string CorrelationId =>
        HttpContext.Items["X-Correlation-ID"]?.ToString() ?? HttpContext.TraceIdentifier;
}
