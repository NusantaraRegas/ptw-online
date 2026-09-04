using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class PermitService(
    IPermitStore store,
    IActorContext actorContext,
    IClock clock,
    IPermitNumberGenerator numberGenerator,
    IOperationalPolicyGate operationalPolicyGate,
    IPermitAttachmentStore attachmentStore,
    AttachmentPolicy attachmentPolicy)
{
    private const string HsseValidatorRole = "HSSEValidator";
    private const string AreaOwnerApproverRole = "AreaOwnerApprover";
    private const string IssuingAuthorityRole = "IssuingAuthority";

    public async Task<PermitResponse> CreateAsync(PermitDraftRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        EnsureLocationScope(actor, request.LocationId);
        if (!string.Equals(actor.Id, request.SponsorId, StringComparison.OrdinalIgnoreCase) && !actor.Roles.Contains("Administrator"))
        {
            throw new InvalidRequestException("permit.sponsor_mismatch", "Sponsor hanya dapat membuat PTW untuk identitasnya sendiri.");
        }

        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.CreateDraft,
            request.LocationId,
            cancellationToken);
        var permit = Permit.CreateDraft(request.ToDomain(), clock.UtcNow);
        return (await store.AddAsync(
            permit,
            actor,
            correlationId,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    public async Task<PermitResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        return stored.ToResponse();
    }

    public async Task<PagedResponse<PermitResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        var sponsorFilter = actor.Roles.Overlaps(
            [
                "Auditor",
                "Administrator",
                HsseValidatorRole,
                AreaOwnerApproverRole,
                IssuingAuthorityRole
            ])
            ? null
            : actor.Id;
        var items = (await store.ListAsync(sponsorFilter, cancellationToken))
            .Where(x => HasLocationScope(actor, x.Permit.Draft.LocationId))
            .Select(x => x.ToResponse())
            .ToArray();
        return new PagedResponse<PermitResponse>(items, items.Length);
    }

    public async Task<PagedResponse<PermitTaskResponse>> ListTasksAsync(CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        var entries = await store.ListPendingTasksAsync(
            actor.Id,
            actor.Roles,
            actor.LocationScopes,
            cancellationToken);
        var items = entries.Select(entry => new PermitTaskResponse(
            entry.Id,
            entry.PermitId,
            entry.PermitVersion,
            entry.Type,
            entry.Label,
            entry.RequiredRole,
            entry.Status,
            entry.PermitNumber,
            entry.PermitTitle,
            entry.LocationId,
            entry.CreatedAt,
            entry.CompletedAt)).ToArray();
        return new PagedResponse<PermitTaskResponse>(items, items.Length);
    }

    public async Task<PagedResponse<PermitActivityResponse>> ListActivityAsync(
        Guid id,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        EnsureValidPage(offset, limit);
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        var page = await store.ListActivityAsync(id, offset, limit, cancellationToken);
        var items = page.Items.Select(entry => new PermitActivityResponse(
            entry.Sequence,
            entry.EventType,
            entry.ActorId,
            entry.OccurredAt,
            JsonSerializer.Deserialize<JsonElement>(entry.PayloadJson),
            entry.CorrelationId)).ToArray();
        return new PagedResponse<PermitActivityResponse>(items, page.Count);
    }

    public async Task<PagedResponse<PermitVersionResponse>> ListVersionsAsync(
        Guid id,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        EnsureValidPage(offset, limit);
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        var page = await store.ListVersionsAsync(id, offset, limit, cancellationToken);
        var items = page.Items.Select(entry => new PermitVersionResponse(
            entry.Version,
            entry.Draft.ToRequest(),
            entry.ContentHash,
            entry.CreatedAt,
            entry.CreatedBy)).ToArray();
        return new PagedResponse<PermitVersionResponse>(items, page.Count);
    }

    public async Task<PermitResponse> UpdateDraftAsync(
        Guid id,
        PermitDraftRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var stored = await GetStoredAsync(id, cancellationToken);
        var actor = actorContext.Current;
        EnsureSponsorOwnership(actor, stored.Permit);
        if (stored.Permit.RenewedFromPermitId is Guid sourcePermitId)
        {
            var source = await GetStoredAsync(sourcePermitId, cancellationToken);
            if (!string.Equals(request.LocationId, source.Permit.Draft.LocationId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(request.SponsorId, source.Permit.Draft.SponsorId, StringComparison.OrdinalIgnoreCase)
                || request.ValidFrom < source.Permit.Draft.ValidUntil)
            {
                throw new InvalidRequestException(
                    "permit.renewal.source_mismatch",
                    "Sponsor, lokasi, dan awal masa renewal harus tetap mengikuti batas PTW asal.");
            }
        }

        EnsureLocationScope(actor, request.LocationId);
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.UpdateDraft,
            request.LocationId,
            cancellationToken);
        stored.Permit.UpdateDraft(request.ToDomain(), clock.UtcNow);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            null,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    public async Task<PermitRenewalResponse> RequestRenewalAsync(
        Guid sourcePermitId,
        RequestPermitRenewalRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        var requestHash = Hash(new { SourcePermitId = sourcePermitId, Request = request });
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            PermitPolicyOperations.RequestRenewal,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            if (prior.Permit.RenewedFromPermitId != sourcePermitId)
            {
                throw new InvalidOperationException("Hasil idempotensi renewal tidak konsisten.");
            }

            var priorSource = await GetStoredAsync(sourcePermitId, cancellationToken);
            EnsureLocationScope(actor, priorSource.Permit.Draft.LocationId);
            return new PermitRenewalResponse(
                priorSource.Permit.Version,
                priorSource.ETag,
                prior.ToResponse());
        }

        var source = await GetStoredAsync(sourcePermitId, cancellationToken);
        EnsureSponsorOwnership(actor, source.Permit);
        EnsureLocationScope(actor, source.Permit.Draft.LocationId);
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.RequestRenewal,
            source.Permit.Draft.LocationId,
            cancellationToken);
        var now = clock.UtcNow;
        var renewal = Permit.CreateRenewal(
            sourcePermitId,
            source.Permit.Draft with
            {
                ValidFrom = request.ValidFrom,
                ValidUntil = request.ValidUntil
            },
            now);
        source.Permit.RequestRenewal(renewal, now);
        var idempotency = new IdempotencyContext(
            actor.Id,
            PermitPolicyOperations.RequestRenewal,
            idempotencyKey,
            requestHash);
        var created = await store.AddRenewalAsync(
            source.Permit,
            renewal,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken);
        return new PermitRenewalResponse(
            created.Source.Permit.Version,
            created.Source.ETag,
            created.Renewal.ToResponse());
    }

    public async Task<PermitResponse> SubmitAsync(
        Guid id,
        SubmitPermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException("idempotency.required", "Header Idempotency-Key wajib untuk command transisi.");
        }

        var actor = actorContext.Current;
        var requestHash = Hash(request);
        const string operation = "SubmitPermit";
        var prior = await store.FindIdempotentResultAsync(actor.Id, operation, idempotencyKey, requestHash, cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureSponsorOwnership(actor, stored.Permit);
        if (attachmentPolicy.Enabled)
        {
            var attachments = await attachmentStore.ListActiveAsync(id, cancellationToken);
            if (attachmentPolicy.RequireMalwareScan
                && attachments.Any(x => !string.Equals(x.ScanStatus, "CLEAN", StringComparison.Ordinal)))
            {
                throw new InvalidRequestException(
                    "attachment.scan_incomplete",
                    "Seluruh lampiran wajib lulus malware scan sebelum PTW diajukan.");
            }
        }

        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.Submit,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        var readiness = new SubmissionReadiness(
            request.ESimiEligible,
            request.RulesEvaluated,
            request.RequiredDocumentsSafe,
            request.MissingRequirements);
        stored.Permit.Submit(numberGenerator.Generate(clock.UtcNow), readiness, clock.UtcNow);
        stored.Permit.StartReview(clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    public Task<PermitResponse> EndorseHsseValidationAsync(
        Guid id,
        EndorsePermitValidationRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ValidateHsse,
            [HsseValidatorRole],
            (permit, actor, now) => permit.EndorseValidation(
                PermitValidationKind.Hsse,
                actor.Id,
                request.Statement,
                now),
            cancellationToken);

    public Task<PermitResponse> ApproveAsync(
        Guid id,
        ApprovePermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Approve,
            [AreaOwnerApproverRole],
            (permit, actor, now) => permit.Approve(actor.Id, request.Statement, now),
            cancellationToken);

    public Task<PermitResponse> RequestRevisionAsync(
        Guid id,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteReviewDispositionAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestRevision,
            (permit, now) => permit.RequestRevision(request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> RejectAsync(
        Guid id,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteReviewDispositionAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Reject,
            (permit, now) => permit.Reject(request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> RequestSuspensionAsync(
        Guid id,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteOwnedSponsorCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestSuspension,
            (permit, actor, now) => permit.RequestSuspension(actor.Id, request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> ApproveSuspensionAsync(
        Guid id,
        ConfirmPermitActionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ApproveSuspension,
            [AreaOwnerApproverRole],
            (permit, actor, now) => permit.ApproveSuspension(actor.Id, request.Statement, now),
            cancellationToken);

    public Task<PermitResponse> DeclareCompletionAsync(
        Guid id,
        ConfirmPermitActionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteOwnedSponsorCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.DeclareCompletion,
            (permit, actor, now) => permit.DeclareCompletion(actor.Id, request.Statement, now),
            cancellationToken);

    public Task<PermitResponse> ConfirmHsseCompletionAsync(
        Guid id,
        ConfirmPermitActionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ConfirmCompletionHsse,
            [HsseValidatorRole],
            (permit, actor, now) => permit.ConfirmCompletion(
                PermitCompletionKind.Hsse,
                actor.Id,
                request.Statement,
                now),
            cancellationToken);

    public Task<PermitResponse> ConfirmAreaOwnerCompletionAsync(
        Guid id,
        ConfirmPermitActionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ConfirmCompletionAreaOwner,
            [AreaOwnerApproverRole],
            (permit, actor, now) => permit.ConfirmCompletion(
                PermitCompletionKind.AreaOwner,
                actor.Id,
                request.Statement,
                now),
            cancellationToken);

    public Task<PermitResponse> CloseAsync(
        Guid id,
        ConfirmPermitActionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Close,
            [AreaOwnerApproverRole],
            (permit, actor, now) => permit.Close(actor.Id, request.Statement, now),
            cancellationToken);

    public async Task<PermitResponse> IssueAsync(
        Guid id,
        IssuePermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        EnsureAnyRole(actor, [AreaOwnerApproverRole]);
        var requestHash = Hash(request);
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            PermitPolicyOperations.Issue,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.Issue,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        var now = clock.UtcNow;
        if (stored.Permit.RenewedFromPermitId is Guid sourcePermitId)
        {
            var source = await GetStoredAsync(sourcePermitId, cancellationToken);
            var sourceEnded = source.Permit.Status is PermitStatus.Closed or PermitStatus.Expired;
            if (!sourceEnded)
            {
                throw new InvalidRequestException(
                    "permit.renewal.source_still_active",
                    "Renewal belum dapat diterbitkan selama PTW asal masih aktif atau belum ditutup.");
            }
        }

        if (!string.Equals(stored.Permit.Approval?.ActorId, actor.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "PTW hanya dapat diterbitkan oleh PIC pemilik area yang menyetujuinya.");
        }

        stored.Permit.OpenWorkPeriod(
            new FieldIssueReadiness(
                request.ESimiEligible,
                request.LocationVerified,
                request.ToolboxTalkComplete,
                request.PersonnelAcknowledged,
                request.PpeAndControlsVerified,
                request.IsolationVerified,
                request.SimopsVerified,
                request.GasTestSatisfied,
                request.HasUnresolvedSuspension),
            actor.Id,
            now);
        var idempotency = new IdempotencyContext(
            actor.Id,
            PermitPolicyOperations.Issue,
            idempotencyKey,
            requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    private async Task<PermitResponse> ExecuteCommandAsync<TRequest>(
        Guid id,
        TRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        string operation,
        IReadOnlyCollection<string> allowedRoles,
        Action<Permit, Actor, DateTimeOffset> execute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib untuk command transisi.");
        }

        var actor = actorContext.Current;
        EnsureAnyRole(actor, allowedRoles);
        var requestHash = Hash(request);
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, actor, clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    private async Task<PermitResponse> ExecuteReviewDispositionAsync(
        Guid id,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        string operation,
        Action<Permit, DateTimeOffset> execute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib untuk command transisi.");
        }

        var actor = actorContext.Current;
        EnsureAnyRole(actor, [HsseValidatorRole, AreaOwnerApproverRole]);
        var requestHash = Hash(request);
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        if (stored.Permit.Status == PermitStatus.UnderReview)
        {
            EnsureAnyRole(actor, [HsseValidatorRole]);
        }
        else if (stored.Permit.Status == PermitStatus.AwaitingApproval)
        {
            EnsureAnyRole(actor, [AreaOwnerApproverRole]);
        }

        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    private async Task<PermitResponse> ExecuteOwnedSponsorCommandAsync<TRequest>(
        Guid id,
        TRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        string operation,
        Action<Permit, Actor, DateTimeOffset> execute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib untuk command transisi.");
        }

        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        var requestHash = Hash(request);
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureSponsorOwnership(actor, stored.Permit);
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, actor, clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    private async Task<StoredPermit> GetStoredAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) ?? throw new ResourceNotFoundException("Permit", id);

    private static void EnsureSponsorOrAdmin(Actor actor)
    {
        if (!actor.Roles.Overlaps(["Sponsor", "Administrator"]))
        {
            throw new UnauthorizedAccessException("Peran Sponsor diperlukan untuk menyusun PTW.");
        }
    }

    private static void EnsureIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib dan maksimum 200 karakter untuk command transisi.");
        }
    }

    private static void EnsureAnyRole(Actor actor, IReadOnlyCollection<string> allowedRoles)
    {
        if (!actor.Roles.Overlaps(allowedRoles))
        {
            throw new UnauthorizedAccessException(
                $"Aksi memerlukan salah satu role: {string.Join(", ", allowedRoles)}.");
        }
    }

    private static void EnsureSponsorOwnership(Actor actor, Permit permit)
    {
        EnsureSponsorOrAdmin(actor);
        if (!string.Equals(actor.Id, permit.Draft.SponsorId, StringComparison.OrdinalIgnoreCase)
            && !actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("PTW berada di luar kepemilikan Sponsor.");
        }
        EnsureLocationScope(actor, permit.Draft.LocationId);
    }

    private static void EnsureLocationScope(Actor actor, string locationId)
    {
        if (!HasLocationScope(actor, locationId))
        {
            throw new UnauthorizedAccessException("Lokasi PTW berada di luar cakupan otorisasi pengguna.");
        }
    }

    private static bool HasLocationScope(Actor actor, string locationId) =>
        actor.LocationScopes.Contains("*") || actor.LocationScopes.Contains(locationId);

    private static void EnsureValidPage(int offset, int limit)
    {
        if (offset < 0)
        {
            throw new InvalidRequestException("pagination.invalid_offset", "Offset tidak boleh negatif.");
        }

        if (limit is < 1 or > 100)
        {
            throw new InvalidRequestException("pagination.invalid_limit", "Limit harus berada antara 1 dan 100.");
        }
    }

    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
