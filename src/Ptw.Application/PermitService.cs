using System.Security.Cryptography;
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
    AttachmentPolicy attachmentPolicy,
    LocationReleaseSettings locationRelease,
    IssuancePolicySettings issuancePolicy,
    IUserDirectoryStore userDirectoryStore)
{
    private const string HseValidatorRole = "HSEValidator";
    // Retain the persisted code for compatibility. This role is the single Bagian 7
    // reviewer pool and may be assigned to an SO or Officer Pemilik Wilayah.
    private const string AreaOperationsReviewerRole = "AreaOwnerSeniorOfficer";
    private const string AreaOwnerManagerRole = "AreaOwnerManager";

    public async Task<PermitResponse> CreateAsync(
        PermitDraftRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        EnsureLocationScope(actor, request.LocationId);
        if (!string.Equals(actor.Id, request.SponsorId, StringComparison.OrdinalIgnoreCase)
            && !actor.Roles.Contains("Administrator"))
        {
            throw new InvalidRequestException(
                "permit.sponsor_mismatch",
                "Sponsor hanya dapat membuat PTW untuk identitasnya sendiri.");
        }

        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.CreateDraft,
            request.LocationId,
            cancellationToken);
        var permit = Permit.CreateDraft(request.ToDomain(), clock.UtcNow);
        var created = await store.AddAsync(permit, actor, correlationId, authorization, cancellationToken);
        return await ToResponseAsync(created, cancellationToken);
    }

    public async Task<PermitResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        return await ToResponseAsync(stored, cancellationToken);
    }

    public async Task<PagedResponse<PermitResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        var sponsorFilter = actor.Roles.Overlaps(
            ["Auditor", "Administrator", HseValidatorRole, AreaOperationsReviewerRole, AreaOwnerManagerRole])
            ? null
            : actor.Id;
        var storedItems = (await store.ListAsync(sponsorFilter, cancellationToken))
            .Where(x => HasLocationScope(actor, x.Permit.Draft.LocationId))
            .ToArray();
        var items = new List<PermitResponse>(storedItems.Length);
        var accountCache = new Dictionary<string, StoredUserAccount?>(StringComparer.OrdinalIgnoreCase);
        foreach (var stored in storedItems)
        {
            items.Add(await ToResponseAsync(stored, cancellationToken, accountCache));
        }

        return new PagedResponse<PermitResponse>(items, items.Count);
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
        return new PagedResponse<PermitActivityResponse>(page.Items.Select(entry => new PermitActivityResponse(
            entry.Sequence,
            entry.EventType,
            entry.ActorId,
            entry.OccurredAt,
            JsonSerializer.Deserialize<JsonElement>(entry.PayloadJson),
            entry.CorrelationId)).ToArray(), page.Count);
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
        return new PagedResponse<PermitVersionResponse>(page.Items.Select(entry => new PermitVersionResponse(
            entry.Version,
            entry.Draft.ToRequest(),
            entry.ContentHash,
            entry.CreatedAt,
            entry.CreatedBy)).ToArray(), page.Count);
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
        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.UpdateDraft,
            request.LocationId,
            cancellationToken);
        stored.Permit.UpdateDraft(request.ToDomain(), clock.UtcNow);
        var updated = await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            null,
            authorization,
            cancellationToken);
        return await ToResponseAsync(updated, cancellationToken);
    }

    public async Task<PermitResponse> RequestRenewalAsync(
        Guid sourcePermitId,
        RequestPermitRenewalRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!request.AllPagesReviewed || !request.ReadableAndCompleteAcknowledged)
        {
            throw new InvalidRequestException(
                "permit.renewal.acknowledgement_required",
                "Sponsor wajib meninjau semua halaman dan mengakui hardcopy hasil verifikasi lapangan terbaca serta lengkap.");
        }

        await EnsureSignedFieldCopyEvidenceAsync(
            sourcePermitId,
            request.PrintPackageId,
            request.SignedFieldCopyAttachmentIds,
            cancellationToken);

        return await ExecuteOwnedSponsorCommandAsync(
            sourcePermitId,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestRenewal,
            (permit, actor, now) => permit.RequestRenewal(
                request.PrintPackageId,
                request.SignedFieldCopyAttachmentIds,
                actor.Id,
                request.ContinuationStatement,
                request.ValidFrom,
                request.ValidUntil,
                now),
            cancellationToken);
    }

    public Task<PermitResponse> RequestRenewalEvidenceReplacementAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteTaskCommandAsync(
            taskId,
            "AREA_RENEWAL_REVIEW",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestRenewalEvidenceReplacement,
            [AreaOwnerManagerRole],
            (permit, actor, now, _) => permit.RequestRenewalEvidenceReplacement(actor.Id, request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> RejectRenewalAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteTaskCommandAsync(
            taskId,
            "AREA_RENEWAL_REVIEW",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RejectRenewal,
            [AreaOwnerManagerRole],
            (permit, actor, now, _) => permit.RejectRenewal(actor.Id, request.Reason, now),
            cancellationToken);

    public async Task<PermitRenewalResponse> ApproveRenewalAsync(
        Guid taskId,
        ApproveRenewalRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!request.FieldVerificationConfirmed || !request.EvidenceReadable)
        {
            throw new InvalidRequestException(
                "permit.renewal.verification_incomplete",
                "Verifikasi lapangan dan keterbacaan hardcopy wajib dikonfirmasi sebelum perpanjangan disetujui.");
        }

        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        EnsureAnyRole(actor, [AreaOwnerManagerRole]);
        var requestHash = Hash(new { TaskId = taskId, Request = request });
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            PermitPolicyOperations.ApproveRenewal,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            var sourceId = prior.Permit.RenewedFromPermitId
                ?? throw new InvalidOperationException("Hasil idempoten approval renewal tidak memiliki PTW asal.");
            var priorSource = await GetStoredAsync(sourceId, cancellationToken);
            return new PermitRenewalResponse(
                priorSource.Permit.Version,
                priorSource.ETag,
                await ToResponseAsync(prior, cancellationToken));
        }

        var task = await GetPendingTaskAsync(taskId, cancellationToken);
        if (!string.Equals(task.Type, "AREA_RENEWAL_REVIEW", StringComparison.Ordinal)
            || !string.Equals(task.RequiredRole, AreaOwnerManagerRole, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException("task.type.invalid", "Task tidak sesuai dengan command approval perpanjangan.");
        }

        var source = await GetStoredAsync(task.PermitId, cancellationToken);
        EnsureLocationScope(actor, source.Permit.Draft.LocationId);
        if (task.PermitVersion != source.Permit.Version)
        {
            throw new ConcurrencyConflictException();
        }

        var evidence = source.Permit.RenewalRequest
            ?? throw new InvalidRequestException("permit.renewal.request_missing", "Permintaan perpanjangan tidak tersedia.");
        await EnsureSignedFieldCopyEvidenceAsync(
            source.Permit.Id,
            evidence.PrintPackageId,
            evidence.AttachmentIds,
            cancellationToken);
        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.ApproveRenewal,
            source.Permit.Draft.LocationId,
            cancellationToken);
        var now = clock.UtcNow;
        var renewal = Permit.CreateRenewal(source.Permit.Id, source.Permit.Draft with
        {
            ValidFrom = evidence.ValidFrom,
            ValidUntil = evidence.ValidUntil,
            SafetyEquipmentCodes = []
        }, now);
        source.Permit.ApproveRenewal(renewal, actor.Id, request.Statement, now);
        var created = await store.AddRenewalAsync(
            source.Permit,
            renewal,
            expectedETag,
            actor,
            correlationId,
            new IdempotencyContext(
                actor.Id,
                PermitPolicyOperations.ApproveRenewal,
                idempotencyKey,
                requestHash),
            authorization,
            cancellationToken);
        return new PermitRenewalResponse(
            created.Source.Permit.Version,
            created.Source.ETag,
            await ToResponseAsync(created.Renewal, cancellationToken));
    }

    public async Task<PermitResponse> SubmitAsync(
        Guid id,
        SubmitPermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        var requestHash = Hash(new { PermitId = id, Request = request });
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            PermitPolicyOperations.Submit,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return await ToResponseAsync(prior, cancellationToken);
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureSponsorOwnership(actor, stored.Permit);
        if (!locationRelease.TryGetAreaOwnerDepartment(
                stored.Permit.Draft.LocationId,
                out _))
        {
            throw new InvalidRequestException(
                "permit.location.not_released",
                $"Lokasi {stored.Permit.Draft.LocationId} belum memiliki release dan konfigurasi pemilik wilayah yang aktif.");
        }

        var attachments = await attachmentStore.ListActiveAsync(id, cancellationToken);
        EnsureSupportingDocumentEvidence(stored.Permit, attachments);
        if (attachmentPolicy.Enabled)
        {
            if (attachmentPolicy.RequireMalwareScan
                && attachments.Any(x => !string.Equals(x.ScanStatus, "CLEAN", StringComparison.Ordinal)))
            {
                throw new InvalidRequestException(
                    "attachment.scan_incomplete",
                    "Seluruh lampiran wajib lulus malware scan sebelum PTW diajukan.");
            }
        }

        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.Submit,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        stored.Permit.Submit(
            numberGenerator.Generate(clock.UtcNow),
            new SubmissionReadiness(
                request.ESimiEligible,
                request.RulesEvaluated,
                request.RequiredDocumentsSafe,
                request.MissingRequirements),
            clock.UtcNow);
        return await PersistCommandAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            PermitPolicyOperations.Submit,
            idempotencyKey,
            requestHash,
            authorization,
            cancellationToken);
    }

    private void EnsureSupportingDocumentEvidence(
        Permit permit,
        IReadOnlyList<PermitAttachmentEntry> attachments)
    {
        if (!attachmentPolicy.Enabled)
        {
            throw new InvalidRequestException(
                "permit.supporting_document.storage_unavailable",
                "Pengajuan diblokir karena penyimpanan dokumen pendukung belum tersedia.");
        }

        var mandatoryDocuments = PermitMandatoryDocumentCatalog.Resolve();
        foreach (var document in mandatoryDocuments)
        {
            EnsureDocumentEvidence(
                attachments,
                document.Code,
                document.Label,
                "permit.mandatory_document.evidence_required",
                "permit.mandatory_document.scan_incomplete");
        }

        var mandatoryCodes = mandatoryDocuments
            .Select(document => document.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedCodes = PermitSupportingDocumentCatalog.NormalizeAndValidate(
            permit.Draft.RequiredDocumentCodes);
        foreach (var code in selectedCodes.Where(code => !mandatoryCodes.Contains(code)))
        {
            EnsureDocumentEvidence(
                attachments,
                code,
                PermitSupportingDocumentCatalog.Resolve(code).Label,
                "permit.supporting_document.evidence_required",
                "permit.supporting_document.scan_incomplete");
        }

        var jsaNumber = permit.Draft.JsaDocumentNumber?.Trim();
        var jsaRevision = permit.Draft.JsaRevision?.Trim();
        var jsaDate = permit.Draft.JsaDate?.UtcDateTime.Date;
        if (string.IsNullOrWhiteSpace(jsaNumber)
            || string.IsNullOrWhiteSpace(jsaRevision)
            || jsaDate is null)
        {
            throw new InvalidRequestException(
                "permit.supporting_document.jsa_metadata_required",
                "Nomor, revisi, dan tanggal JSA wajib lengkap sebelum PTW diajukan.");
        }

        if (!attachments.Any(attachment =>
                string.Equals(
                    EffectiveSupportingDocumentCode(attachment),
                    PermitSupportingDocumentCatalog.JsaCode,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(attachment.DocumentNumber?.Trim(), jsaNumber, StringComparison.OrdinalIgnoreCase)
                && string.Equals(attachment.DocumentRevision?.Trim(), jsaRevision, StringComparison.OrdinalIgnoreCase)
                && attachment.DocumentDate?.UtcDateTime.Date == jsaDate))
        {
            throw new InvalidRequestException(
                "permit.supporting_document.jsa_metadata_mismatch",
                "Metadata lampiran JSA harus sama dengan nomor, revisi, dan tanggal JSA pada draft.");
        }
    }

    private void EnsureDocumentEvidence(
        IReadOnlyList<PermitAttachmentEntry> attachments,
        string code,
        string label,
        string evidenceRequiredCode,
        string scanIncompleteCode)
    {
        var evidence = attachments.Where(attachment =>
            string.Equals(
                EffectiveSupportingDocumentCode(attachment),
                code,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (evidence.Length == 0)
        {
            throw new InvalidRequestException(
                evidenceRequiredCode,
                $"Lampiran untuk {label} wajib tersedia sebelum PTW diajukan.");
        }

        if (attachmentPolicy.RequireMalwareScan
            && evidence.All(item => !string.Equals(item.ScanStatus, "CLEAN", StringComparison.Ordinal)))
        {
            throw new InvalidRequestException(
                scanIncompleteCode,
                $"Lampiran untuk {label} belum dinyatakan aman.");
        }
    }

    private static string? EffectiveSupportingDocumentCode(PermitAttachmentEntry attachment) =>
        attachment.SupportingDocumentCode
        ?? (string.Equals(attachment.Category, "JSA", StringComparison.Ordinal)
            ? PermitSupportingDocumentCatalog.JsaCode
            : null);

    public async Task<PermitResponse> ValidateSubmissionAsync(
        Guid taskId,
        ValidateSubmissionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var presentation = await ResolveActorPresentationAsync(actorContext.Current.Id, cancellationToken);
        return await ExecuteTaskCommandAsync(
            taskId,
            "HSE_VALIDATION",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ValidateSubmission,
            [HseValidatorRole],
            (permit, actor, now, _) => permit.ValidateSubmission(
                actor.Id,
                request.Statement,
                request.SafetyEquipmentCodes,
                now,
                DisplayNameUnlessIdentifier(actor, presentation.DisplayName)),
            cancellationToken);
    }

    public Task<PermitResponse> EscalateValidationAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteTaskCommandAsync(
            taskId,
            "HSE_VALIDATION",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.EscalateValidation,
            [HseValidatorRole],
            (permit, actor, now, _) => permit.EscalateValidation(actor.Id, request.Reason, now),
            cancellationToken);

    public async Task<PermitResponse> ApproveAndIssueAsync(
        Guid taskId,
        ApproveAndIssuePermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var presentation = await ResolveActorPresentationAsync(actorContext.Current.Id, cancellationToken);
        return await ExecuteTaskCommandAsync(
            taskId,
            "AREA_APPROVE_AND_ISSUE",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ApproveAndIssue,
            [AreaOwnerManagerRole],
            (permit, actor, now, authorization) =>
            {
                if (!issuancePolicy.IsReady)
                {
                    throw new PolicyActivationException(
                        "Penerbitan diblokir: ruleset, template cetak, dan campaign asset yang disahkan belum dikonfigurasi.");
                }

                if (request.ActingAssignmentId is not null)
                {
                    throw new PolicyAuthorizationDeniedException(
                        "authorization.acting_assignment_not_ready",
                        "Approval sebagai pengganti diblokir sampai resolver acting assignment v1.6 dapat memverifikasi principal, dokumen, maker-checker, scope, risiko, dan status pencabutan.");
                }

                var authorizationId = authorization?.AssignmentIds.SingleOrDefault() ?? Guid.Empty;
                if (authorizationId == Guid.Empty && actor.IsDevelopment)
                {
                    authorizationId = DevelopmentAuthorizationId(actor.Id);
                }

                if (authorizationId == Guid.Empty)
                {
                    throw new PolicyAuthorizationDeniedException(
                        "authorization.manager_assignment_required",
                        "Assignment Manager aktif yang terverifikasi server wajib tersedia.");
                }

                if (now > permit.Draft.ValidUntil)
                {
                    throw new DomainRuleViolationException(
                        "permit.outside_validity",
                        "PTW tidak dapat diterbitkan setelah masa berlakunya berakhir.");
                }

                var actorName = DisplayNameUnlessIdentifier(actor, presentation.DisplayName)
                    ?? "Profil pengguna tidak tersedia";
                var actorPosition = presentation.Position ?? AreaManagerPosition(permit.Draft.LocationId);
                permit.ApproveAndIssue(new PermitApprovalEvidence(
                    actor.Id,
                    actorPosition,
                    ApprovalCapacity.Manager,
                    actor.Id,
                    actorPosition,
                    authorizationId,
                    null,
                    issuancePolicy.RuleVersion,
                    issuancePolicy.PrintTemplateVersion,
                    issuancePolicy.CampaignAssetVersion,
                    request.Statement,
                    now,
                    actorName,
                    presentation.Signature), now);
            },
            cancellationToken);
    }

    public async Task<PermitResponse> ReviewAreaOperationsAsync(
        Guid taskId,
        ReviewAreaOperationsRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var presentation = await ResolveActorPresentationAsync(actorContext.Current.Id, cancellationToken);
        return await ExecuteTaskCommandAsync(
            taskId,
            "AREA_OPERATION_REVIEW",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ReviewAreaOperations,
            [AreaOperationsReviewerRole],
            (permit, actor, now, authorization) =>
            {
                if (!request.ConditionsReviewed)
                {
                    throw new DomainRuleViolationException(
                        "permit.area_operations.confirmation_required",
                        "SO atau Officer Pemilik Wilayah wajib mengonfirmasi bahwa seluruh kondisi operasi yang relevan telah diperiksa.");
                }

                var authorizationId = authorization?.AssignmentIds.SingleOrDefault() ?? Guid.Empty;
                if (authorizationId == Guid.Empty && actor.IsDevelopment)
                {
                    authorizationId = DevelopmentAuthorizationId(actor.Id);
                }

                if (authorizationId == Guid.Empty)
                {
                    throw new PolicyAuthorizationDeniedException(
                        "authorization.area_operations_reviewer_assignment_required",
                        "Assignment SO/Officer Pemilik Wilayah aktif yang terverifikasi server wajib tersedia.");
                }

                var actorName = DisplayNameUnlessIdentifier(actor, presentation.DisplayName)
                    ?? "Profil pengguna tidak tersedia";
                var actorPosition = presentation.Position ?? AreaOperationsPosition(permit.Draft.LocationId);
                permit.ReviewAreaOperations(new AreaOperationsReviewEvidence(
                    actor.Id,
                    actorName,
                    actorPosition,
                    authorizationId,
                    request.ConditionCodes,
                    request.OtherConditionDetail,
                    request.Statement,
                    now,
                    presentation.Signature), now);
            },
            cancellationToken);
    }

    private async Task<ActorPresentation> ResolveActorPresentationAsync(
        string actorId,
        CancellationToken cancellationToken)
    {
        var stored = await userDirectoryStore.FindAsync(actorId, cancellationToken);
        var account = stored?.Account.IsActive == true ? stored.Account : null;
        var signature = stored?.Signature is { IsActive: true } activeSignature
            ? new VisualSignatureEvidence(
                activeSignature.Id,
                activeSignature.Version,
                activeSignature.MediaType,
                activeSignature.Content,
                activeSignature.Sha256)
            : null;
        return new ActorPresentation(
            NormalizeOptional(account?.DisplayName),
            NormalizeOptional(account?.Position),
            signature);
    }

    private sealed record ActorPresentation(
        string? DisplayName,
        string? Position,
        VisualSignatureEvidence? Signature);

    public Task<PermitResponse> RequestRevisionAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteDispositionAsync(
            taskId,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestRevision,
            (permit, now) => permit.RequestRevision(request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> RejectAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteDispositionAsync(
            taskId,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Reject,
            (permit, now) => permit.Reject(request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> SuspendAsync(
        Guid id,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecutePermitCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Suspend,
            [HseValidatorRole, AreaOwnerManagerRole, "Administrator"],
            (permit, actor, now) => permit.Suspend(actor.Id, request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> ResolveSuspensionAsync(
        Guid id,
        ResolveSuspensionRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecutePermitCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ResolveSuspension,
            [AreaOwnerManagerRole, "Administrator"],
            (permit, actor, now) => permit.ResolveSuspension(actor.Id, request.Resolution, now),
            cancellationToken);

    public async Task<PermitResponse> RequestClosureAsync(
        Guid id,
        RequestClosureRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await ValidateClosureEvidenceAsync(id, request, cancellationToken);

        return await ExecuteOwnedSponsorCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestClosure,
            (permit, actor, now) => permit.RequestClosure(
                request.PrintPackageId,
                request.SignedFieldCopyAttachmentIds,
                actor.Id,
                request.CompletionStatement,
                now),
            cancellationToken);
    }

    public async Task<PermitResponse> ResubmitClosureAsync(
        Guid id,
        RequestClosureRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await ValidateClosureEvidenceAsync(id, request, cancellationToken);

        return await ExecuteOwnedSponsorCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ResubmitClosure,
            (permit, actor, now) => permit.ResubmitClosure(
                request.PrintPackageId,
                request.SignedFieldCopyAttachmentIds,
                actor.Id,
                request.CompletionStatement,
                now),
            cancellationToken);
    }

    public Task<PermitResponse> RequestClosureEvidenceReplacementAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteTaskCommandAsync(
            taskId,
            "AREA_CLOSE_VERIFICATION",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.RequestClosureEvidenceReplacement,
            [AreaOwnerManagerRole],
            (permit, actor, now, _) => permit.RequestClosureEvidenceReplacement(actor.Id, request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> CloseAsync(
        Guid taskId,
        ClosePermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OfficerName)
            || !request.WorkAreaInspectedAndClean
            || !request.WorkCompleted
            || !request.ManagerAgreesWorkCompleted
            || !request.InhibitedSystemsRestored
            || !request.AreaHandedBackAndSafeguardsRestored
            || !request.EvidenceReadable)
        {
            throw new InvalidRequestException(
                "permit.closure.verification_incomplete",
                "Seluruh checklist Bagian 10, nama Officer, dan keterbacaan hardcopy wajib dikonfirmasi sebelum menutup PTW.");
        }

        return ExecuteTaskCommandAsync(
            taskId,
            "AREA_CLOSE_VERIFICATION",
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Close,
            [AreaOwnerManagerRole],
            (permit, actor, now, _) => permit.Close(
                actor.Id,
                request.Statement,
                request.OfficerName,
                request.WorkAreaInspectedAndClean,
                request.WorkCompleted,
                request.ManagerAgreesWorkCompleted,
                request.InhibitedSystemsRestored,
                request.AreaHandedBackAndSafeguardsRestored,
                request.EvidenceReadable,
                now),
            cancellationToken);
    }

    public Task<PermitResponse> CancelAsync(
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
            PermitPolicyOperations.Cancel,
            (permit, _, now) => permit.Cancel(request.Reason, now),
            cancellationToken);

    public Task<PermitResponse> ExpireAsync(
        Guid id,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecutePermitCommandAsync(
            id,
            new { },
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Expire,
            ["Administrator"],
            (permit, _, now) => permit.Expire(now),
            cancellationToken);

    private async Task ValidateClosureEvidenceAsync(
        Guid permitId,
        RequestClosureRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.AllPagesReviewed || !request.ReadableAndCompleteAcknowledged)
        {
            throw new InvalidRequestException(
                "permit.closure.acknowledgement_required",
                "Sponsor wajib meninjau semua halaman dan mengakui hardcopy terbaca serta lengkap.");
        }

        var printPackage = await store.FindPrintPackageAsync(request.PrintPackageId, cancellationToken);
        if (printPackage is null
            || printPackage.PermitId != permitId
            || !string.Equals(printPackage.RenderStatus, "READY", StringComparison.Ordinal))
        {
            throw new InvalidRequestException(
                "permit.closure.print_package_invalid",
                "PrintPackage resmi harus READY dan berasal dari PTW yang sama.");
        }

        if (request.SignedFieldCopyAttachmentIds is null
            || request.SignedFieldCopyAttachmentIds.Count == 0
            || request.SignedFieldCopyAttachmentIds.Count
                != request.SignedFieldCopyAttachmentIds.Distinct().Count())
        {
            throw new InvalidRequestException(
                "permit.closure.evidence_required",
                "Sedikitnya satu SIGNED_FIELD_COPY unik wajib dipilih.");
        }

        var attachments = await attachmentStore.ListActiveAsync(permitId, cancellationToken);
        var selected = attachments
            .Where(x => request.SignedFieldCopyAttachmentIds.Contains(x.Id))
            .ToArray();
        if (selected.Length != request.SignedFieldCopyAttachmentIds.Count
            || selected.Any(x => !string.Equals(x.Category, "SIGNED_FIELD_COPY", StringComparison.Ordinal)
                || !string.Equals(x.ScanStatus, "CLEAN", StringComparison.Ordinal)
                || x.TargetPermitVersion != printPackage.PermitVersion
                || x.PrintPackageId != printPackage.Id
                || string.IsNullOrWhiteSpace(x.DocumentNumber)
                || string.IsNullOrWhiteSpace(x.DocumentRevision)
                || x.DocumentDate is null)
            || selected.Any(x => attachments.Any(candidate => candidate.SupersedesAttachmentId == x.Id)))
        {
            throw new InvalidRequestException(
                "permit.closure.evidence_invalid",
                "SIGNED_FIELD_COPY harus CLEAN, aktif, tidak superseded, bermetadata lengkap, dan cocok dengan PermitVersion serta PrintPackage.");
        }
    }

    private async Task EnsureSignedFieldCopyEvidenceAsync(
        Guid permitId,
        Guid printPackageId,
        IReadOnlyList<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        var printPackage = await store.FindPrintPackageAsync(printPackageId, cancellationToken);
        if (printPackage is null
            || printPackage.PermitId != permitId
            || !string.Equals(printPackage.RenderStatus, "READY", StringComparison.Ordinal))
        {
            throw new InvalidRequestException(
                "permit.renewal.print_package_invalid",
                "PrintPackage resmi harus READY dan berasal dari PTW yang sama.");
        }

        if (attachmentIds.Count == 0 || attachmentIds.Count != attachmentIds.Distinct().Count())
        {
            throw new InvalidRequestException(
                "permit.renewal.evidence_required",
                "Sedikitnya satu SIGNED_FIELD_COPY unik wajib dipilih.");
        }

        var attachments = await attachmentStore.ListActiveAsync(permitId, cancellationToken);
        var selected = attachments.Where(x => attachmentIds.Contains(x.Id)).ToArray();
        if (selected.Length != attachmentIds.Count
            || selected.Any(x => !string.Equals(x.Category, "SIGNED_FIELD_COPY", StringComparison.Ordinal)
                || !string.Equals(x.ScanStatus, "CLEAN", StringComparison.Ordinal)
                || x.TargetPermitVersion != printPackage.PermitVersion
                || x.PrintPackageId != printPackage.Id
                || string.IsNullOrWhiteSpace(x.DocumentNumber)
                || string.IsNullOrWhiteSpace(x.DocumentRevision)
                || x.DocumentDate is null)
            || selected.Any(x => attachments.Any(candidate => candidate.SupersedesAttachmentId == x.Id)))
        {
            throw new InvalidRequestException(
                "permit.renewal.evidence_invalid",
                "SIGNED_FIELD_COPY harus CLEAN, aktif, tidak superseded, bermetadata lengkap, dan cocok dengan PermitVersion serta PrintPackage.");
        }
    }

    private async Task<PermitResponse> ExecuteDispositionAsync(
        Guid taskId,
        PermitReasonRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        string operation,
        Action<Permit, DateTimeOffset> execute,
        CancellationToken cancellationToken)
    {
        var task = await GetPendingTaskAsync(taskId, cancellationToken);
        var role = task.Type switch
        {
            "HSE_VALIDATION" => HseValidatorRole,
            "AREA_OPERATION_REVIEW" => AreaOperationsReviewerRole,
            "AREA_APPROVE_AND_ISSUE" => AreaOwnerManagerRole,
            _ => throw new InvalidRequestException("task.type.invalid", "Task tidak mendukung keputusan revisi atau penolakan.")
        };
        return await ExecuteTaskCommandAsync(
            taskId,
            task.Type,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            operation,
            [role],
            (permit, _, now, _) => execute(permit, now),
            cancellationToken);
    }

    private async Task<PermitResponse> ExecuteTaskCommandAsync<TRequest>(
        Guid taskId,
        string expectedTaskType,
        TRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        string operation,
        IReadOnlyCollection<string> allowedRoles,
        Action<Permit, Actor, DateTimeOffset, PolicyAuthorizationEvidence?> execute,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        EnsureAnyRole(actor, allowedRoles);
        var requestHash = Hash(new { TaskId = taskId, Request = request });
        var prior = await store.FindIdempotentResultAsync(actor.Id, operation, idempotencyKey, requestHash, cancellationToken);
        if (prior is not null)
        {
            return await ToResponseAsync(prior, cancellationToken);
        }

        var task = await GetPendingTaskAsync(taskId, cancellationToken);
        if (!string.Equals(task.Type, expectedTaskType, StringComparison.Ordinal)
            || !string.Equals(task.RequiredRole, allowedRoles.Single(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException("task.type.invalid", "Task tidak sesuai dengan command yang diminta.");
        }

        var stored = await GetStoredAsync(task.PermitId, cancellationToken);
        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        if (task.PermitVersion != stored.Permit.Version)
        {
            throw new ConcurrencyConflictException();
        }

        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, actor, clock.UtcNow, authorization);
        return await PersistCommandAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            operation,
            idempotencyKey,
            requestHash,
            authorization,
            cancellationToken);
    }

    private async Task<PermitResponse> ExecutePermitCommandAsync<TRequest>(
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
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        EnsureAnyRole(actor, allowedRoles);
        var requestHash = Hash(new { PermitId = id, Request = request });
        var prior = await store.FindIdempotentResultAsync(actor.Id, operation, idempotencyKey, requestHash, cancellationToken);
        if (prior is not null)
        {
            return await ToResponseAsync(prior, cancellationToken);
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, actor, clock.UtcNow);
        return await PersistCommandAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            operation,
            idempotencyKey,
            requestHash,
            authorization,
            cancellationToken);
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
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        var requestHash = Hash(new { PermitId = id, Request = request });
        var prior = await store.FindIdempotentResultAsync(actor.Id, operation, idempotencyKey, requestHash, cancellationToken);
        if (prior is not null)
        {
            return await ToResponseAsync(prior, cancellationToken);
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureSponsorOwnership(actor, stored.Permit);
        var authorization = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, actor, clock.UtcNow);
        return await PersistCommandAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            operation,
            idempotencyKey,
            requestHash,
            authorization,
            cancellationToken);
    }

    private async Task<PermitResponse> PersistCommandAsync(
        Permit permit,
        string expectedETag,
        Actor actor,
        string correlationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        PolicyAuthorizationEvidence? authorization,
        CancellationToken cancellationToken)
    {
        var updated = await store.UpdateAsync(
            permit,
            expectedETag,
            actor,
            correlationId,
            new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash),
            authorization,
            cancellationToken);
        return await ToResponseAsync(updated, cancellationToken);
    }

    private async Task<PermitResponse> ToResponseAsync(
        StoredPermit stored,
        CancellationToken cancellationToken,
        Dictionary<string, StoredUserAccount?>? accountCache = null)
    {
        accountCache ??= new Dictionary<string, StoredUserAccount?>(StringComparer.OrdinalIgnoreCase);
        var response = stored.ToResponse();
        var workflow = response.Workflow;
        var sponsorName = await ResolveActorNameAsync(
            response.Draft.SponsorId,
            null,
            accountCache,
            cancellationToken);
        var hseName = await ResolveActorNameAsync(
            workflow.Hse.ActorId,
            workflow.Hse.ActorName,
            accountCache,
            cancellationToken);
        var areaOperationsName = await ResolveActorNameAsync(
            workflow.AreaOperations.ActorId,
            workflow.AreaOperations.ActorName,
            accountCache,
            cancellationToken);
        var approvalName = await ResolveActorNameAsync(
            workflow.Approval.ActorId,
            workflow.Approval.ActorName,
            accountCache,
            cancellationToken);

        return response with
        {
            SponsorName = sponsorName,
            Workflow = workflow with
            {
                Hse = workflow.Hse with { ActorName = hseName },
                AreaOperations = workflow.AreaOperations with { ActorName = areaOperationsName },
                Approval = workflow.Approval with { ActorName = approvalName }
            }
        };
    }

    private async Task<string?> ResolveActorNameAsync(
        string? actorId,
        string? evidenceName,
        Dictionary<string, StoredUserAccount?> accountCache,
        CancellationToken cancellationToken)
    {
        var normalizedActorId = NormalizeOptional(actorId);
        var normalizedEvidenceName = NormalizeOptional(evidenceName);
        if (normalizedActorId is null)
        {
            return normalizedEvidenceName;
        }

        if (!accountCache.TryGetValue(normalizedActorId, out var stored))
        {
            stored = await userDirectoryStore.FindAsync(normalizedActorId, cancellationToken);
            accountCache[normalizedActorId] = stored;
        }

        var userName = NormalizeOptional(stored?.Account.UserName);
        if (!IsLoginIdentifier(normalizedEvidenceName, normalizedActorId, userName))
        {
            return normalizedEvidenceName;
        }

        var profileName = NormalizeOptional(stored?.Account.DisplayName);
        return IsLoginIdentifier(profileName, normalizedActorId, userName) ? null : profileName;
    }

    private static string? DisplayNameUnlessIdentifier(Actor actor, string? preferredName = null)
    {
        var name = NormalizeOptional(preferredName) ?? NormalizeOptional(actor.DisplayName);
        return string.Equals(name, actor.Id, StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    private static bool IsLoginIdentifier(string? value, string actorId, string? userName) =>
        value is null
        || string.Equals(value, actorId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, userName, StringComparison.OrdinalIgnoreCase);

    private async Task<StoredPermit> GetStoredAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) ?? throw new ResourceNotFoundException("Permit", id);

    private async Task<PermitTaskEntry> GetPendingTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        await store.FindPendingTaskAsync(taskId, cancellationToken) ?? throw new ResourceNotFoundException("Task", taskId);

    private static Guid DevelopmentAuthorizationId(string actorId) =>
        new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"ptw-development:{actorId}"))[..16]);

    private static string AreaOperationsPosition(string locationId) =>
        string.Equals(locationId, "ORF", StringComparison.OrdinalIgnoreCase)
            ? "Senior Officer Distribusi Gas dan Manajemen ORF"
            : "Senior Officer Pemilik Wilayah";

    private static string AreaManagerPosition(string locationId) =>
        string.Equals(locationId, "ORF", StringComparison.OrdinalIgnoreCase)
            ? "Kepala Departemen Distribusi Gas dan Manajemen ORF"
            : "Manager Pemilik Wilayah";

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            throw new UnauthorizedAccessException($"Aksi memerlukan salah satu role: {string.Join(", ", allowedRoles)}.");
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

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
