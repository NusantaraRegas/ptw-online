namespace Ptw.Domain;

public sealed class Permit
{
    private readonly List<DomainEvent> _events = [];

    private Permit(Guid id, PermitDraft draft, DateTimeOffset createdAt)
    {
        Id = id;
        Draft = NormalizeAndValidate(draft);
        Status = PermitStatus.Draft;
        Version = 1;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }
    public string? PermitNumber { get; private set; }
    public PermitStatus Status { get; private set; }
    public int Version { get; private set; }
    public PermitDraft Draft { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? RenewedFromPermitId { get; private set; }
    public Guid? RenewalPermitId { get; private set; }
    public string? SuspensionReason { get; private set; }
    public PermitValidationEvidence? HseValidation { get; private set; }
    public PermitApprovalEvidence? Approval { get; private set; }
    public PermitSuspensionEvidence? Suspension { get; private set; }
    public PermitClosureEvidence? ClosureRequest { get; private set; }
    public PermitClosureDecisionEvidence? ClosureDecision { get; private set; }
    public PermitRenewalRequestEvidence? RenewalRequest { get; private set; }
    public IReadOnlyList<DomainEvent> Events => _events;

    public static Permit CreateDraft(PermitDraft draft, DateTimeOffset now)
    {
        EnsureSafetyEquipmentIsAssignedByHse(draft);
        var permit = new Permit(Guid.CreateVersion7(), draft, now);
        permit.Raise("permit_draft_created", new { permit.Version });
        return permit;
    }

    public static Permit CreateRenewal(Guid sourcePermitId, PermitDraft draft, DateTimeOffset now)
    {
        if (sourcePermitId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.source_required",
                "PTW asal wajib tersedia untuk membuat renewal.");
        }

        EnsureSafetyEquipmentIsAssignedByHse(draft);
        var permit = new Permit(Guid.CreateVersion7(), draft, now)
        {
            RenewedFromPermitId = sourcePermitId
        };
        permit.Raise("permit_renewal_draft_created", new { SourcePermitId = sourcePermitId, permit.Version });
        return permit;
    }

    public static Permit Rehydrate(
        Guid id,
        string? permitNumber,
        PermitStatus status,
        int version,
        PermitDraft draft,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? suspensionReason = null,
        PermitValidationEvidence? hseValidation = null,
        PermitApprovalEvidence? approval = null,
        PermitSuspensionEvidence? suspension = null,
        PermitClosureEvidence? closureRequest = null,
        PermitClosureDecisionEvidence? closureDecision = null,
        PermitRenewalRequestEvidence? renewalRequest = null,
        Guid? renewedFromPermitId = null,
        Guid? renewalPermitId = null) =>
        new(id, NormalizeAndValidate(draft, allowLegacyIncompleteDraft: true), createdAt, draftIsNormalized: true)
        {
            PermitNumber = permitNumber,
            Status = status,
            Version = version,
            UpdatedAt = updatedAt.ToUniversalTime(),
            SuspensionReason = suspensionReason,
            HseValidation = hseValidation,
            Approval = approval,
            Suspension = suspension,
            ClosureRequest = closureRequest,
            ClosureDecision = closureDecision,
            RenewalRequest = renewalRequest,
            RenewedFromPermitId = renewedFromPermitId,
            RenewalPermitId = renewalPermitId
        };

    public void RequestRenewal(
        Guid printPackageId,
        IReadOnlyList<Guid> attachmentIds,
        string actorId,
        string continuationStatement,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Expired);
        EnsureEvidence(actorId, continuationStatement);
        if (!string.Equals(Draft.SponsorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.renewal.sponsor_mismatch",
                "Hanya Sponsor PTW yang dapat mengajukan perpanjangan.");
        }

        if (ClosureRequest is not null)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.closure_conflict",
                "Perpanjangan tidak dapat diajukan setelah proses penutupan dimulai.");
        }

        if (RenewalPermitId is not null)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.already_requested",
                "Renewal untuk PTW ini sudah pernah diajukan.");
        }

        if (RenewalRequest?.Status == PermitRenewalReviewStatus.Pending)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.review_pending",
                "Permintaan perpanjangan masih menunggu keputusan Pemilik Wilayah.");
        }

        if (RenewalRequest?.Status == PermitRenewalReviewStatus.RevisionRequired
            && RenewalRequest.AttachmentIds.ToHashSet().SetEquals(attachmentIds))
        {
            throw new DomainRuleViolationException(
                "permit.renewal.evidence_not_replaced",
                "Evidence perpanjangan harus diganti dengan hardcopy hasil verifikasi lapangan yang baru.");
        }

        if (printPackageId == Guid.Empty || attachmentIds.Count == 0 || attachmentIds.Any(x => x == Guid.Empty))
        {
            throw new DomainRuleViolationException(
                "permit.renewal.evidence_required",
                "Paket cetak dan hardcopy hasil verifikasi lapangan wajib dipilih sebelum mengajukan perpanjangan.");
        }

        var normalizedFrom = validFrom.ToUniversalTime();
        var normalizedUntil = validUntil.ToUniversalTime();
        if (normalizedFrom < Draft.ValidUntil)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.validity_overlap",
                "Masa renewal harus dimulai pada atau setelah masa berlaku PTW asal berakhir.");
        }

        if (normalizedFrom >= normalizedUntil)
        {
            throw new DomainRuleViolationException(
                "permit.invalid_validity",
                "Waktu mulai renewal harus lebih awal daripada waktu selesai.");
        }

        if (normalizedUntil - normalizedFrom > TimeSpan.FromDays(7))
        {
            throw new DomainRuleViolationException(
                "permit.validity_exceeds_seven_days",
                "Masa berlaku PTW renewal maksimum tujuh hari.");
        }

        RenewalRequest = new PermitRenewalRequestEvidence(
            printPackageId,
            attachmentIds.Distinct().ToArray(),
            actorId.Trim(),
            continuationStatement.Trim(),
            normalizedFrom,
            normalizedUntil,
            now.ToUniversalTime(),
            (RenewalRequest?.Revision ?? 0) + 1,
            PermitRenewalReviewStatus.Pending);
        Version++;
        Touch(now);
        Raise("permit_renewal_requested", new
        {
            PrintPackageId = printPackageId,
            AttachmentIds = RenewalRequest.AttachmentIds,
            RenewalRequest.RequestedBy,
            RenewalRequest.ContinuationStatement,
            RenewalValidFrom = normalizedFrom,
            RenewalValidUntil = normalizedUntil,
            RenewalRequest.Revision,
            Version
        });
    }

    public void RequestRenewalEvidenceReplacement(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Expired);
        EnsureEvidence(actorId, reason);
        EnsurePendingRenewalRequest();
        RenewalRequest = RenewalRequest! with
        {
            Status = PermitRenewalReviewStatus.RevisionRequired,
            ReplacementReason = reason.Trim(),
            DecidedBy = actorId.Trim(),
            DecisionStatement = reason.Trim(),
            DecidedAt = now.ToUniversalTime()
        };
        Touch(now);
        Raise("renewal_evidence_replacement_requested", new
        {
            RequestedBy = actorId.Trim(),
            Reason = reason.Trim(),
            RenewalRequest.Revision
        });
    }

    public void RejectRenewal(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Expired);
        EnsureEvidence(actorId, reason);
        EnsurePendingRenewalRequest();
        RenewalRequest = RenewalRequest! with
        {
            Status = PermitRenewalReviewStatus.Rejected,
            DecidedBy = actorId.Trim(),
            DecisionStatement = reason.Trim(),
            DecidedAt = now.ToUniversalTime()
        };
        Touch(now);
        Raise("renewal_rejected", new { RejectedBy = actorId.Trim(), Reason = reason.Trim() });
    }

    public void ApproveRenewal(Permit renewal, string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Expired);
        EnsureEvidence(actorId, statement);
        EnsurePendingRenewalRequest();
        if (renewal.RenewedFromPermitId != Id
            || !string.Equals(renewal.Draft.SponsorId, Draft.SponsorId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(renewal.Draft.LocationId, Draft.LocationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.renewal.source_mismatch",
                "Draft renewal harus terhubung ke Sponsor dan lokasi PTW asal.");
        }

        if (renewal.Draft.ValidFrom != RenewalRequest!.ValidFrom
            || renewal.Draft.ValidUntil != RenewalRequest.ValidUntil)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.validity_mismatch",
                "Masa berlaku draft renewal harus sama dengan permintaan yang disetujui.");
        }

        RenewalPermitId = renewal.Id;
        RenewalRequest = RenewalRequest with
        {
            Status = PermitRenewalReviewStatus.Approved,
            DecidedBy = actorId.Trim(),
            DecisionStatement = statement.Trim(),
            DecidedAt = now.ToUniversalTime()
        };
        Touch(now);
        Raise("renewal_approved", new
        {
            RenewalPermitId = renewal.Id,
            ApprovedBy = actorId.Trim(),
            Statement = statement.Trim(),
            RenewalValidFrom = renewal.Draft.ValidFrom,
            RenewalValidUntil = renewal.Draft.ValidUntil
        });
    }

    public void UpdateDraft(PermitDraft draft, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        EnsureSafetyEquipmentIsAssignedByHse(draft);
        Draft = NormalizeAndValidate(draft);
        ClearReviewEvidence();
        Version++;
        Touch(now);
        Raise("permit_draft_updated", new { Version });
    }

    public void AddAttachment(Guid attachmentId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        Version++;
        Touch(now);
        Raise("permit_attachment_added", new { AttachmentId = attachmentId, Version });
    }

    public void AddSignedFieldCopy(Guid attachmentId, Guid printPackageId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Suspended, PermitStatus.Expired);
        if (attachmentId == Guid.Empty || printPackageId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "permit.closure.attachment_reference_required",
                "Attachment dan PrintPackage wajib tersedia untuk signed field copy.");
        }

        if (RenewalRequest?.Status == PermitRenewalReviewStatus.Pending)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.review_pending",
                "Lampiran tidak dapat diubah selama permintaan perpanjangan sedang ditinjau.");
        }

        Version++;
        Touch(now);
        Raise("signed_field_copy_uploaded", new { AttachmentId = attachmentId, PrintPackageId = printPackageId, Version });
    }

    public void RemoveAttachment(Guid attachmentId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        Version++;
        Touch(now);
        Raise("permit_attachment_removed", new { AttachmentId = attachmentId, Version });
    }

    public void Submit(string permitNumber, SubmissionReadiness readiness, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        EnsureHeaderClassificationIsComplete(Draft);
        EnsureOtherWorkTypeDetailIsComplete(Draft);
        EnsureSupportingDocumentSelectionIsComplete(Draft);
        if (!readiness.IsReady)
        {
            throw new DomainRuleViolationException(
                "permit.submit.requirements_incomplete",
                "PTW belum memenuhi seluruh persyaratan submit.");
        }

        if (string.IsNullOrWhiteSpace(permitNumber))
        {
            throw new DomainRuleViolationException("permit.number.required", "Nomor PTW resmi wajib tersedia saat submit.");
        }

        PermitNumber ??= permitNumber.Trim();
        ClearReviewEvidence();
        MoveTo(PermitStatus.UnderValidation, "permit_submitted", now);
    }

    public void ValidateSubmission(
        string actorId,
        string statement,
        IReadOnlyList<string> safetyEquipmentCodes,
        DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderValidation);
        EnsureOtherWorkTypeDetailIsComplete(Draft);
        EnsureEvidence(actorId, statement);
        if (string.Equals(Draft.SponsorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.validation.self_validation_forbidden",
                "Sponsor PTW tidak boleh memvalidasi pengajuannya sendiri.");
        }

        var selectedSafetyEquipment = PermitSafetyEquipmentCatalog.NormalizeAndValidate(
            Draft.PermitClass,
            safetyEquipmentCodes);
        HseValidation = new PermitValidationEvidence(
            actorId.Trim(),
            statement.Trim(),
            now.ToUniversalTime(),
            selectedSafetyEquipment);
        MoveTo(PermitStatus.AwaitingAreaApproval, "hse_validation_completed", now, new
        {
            HseValidation.ActorId,
            HseValidation.Statement,
            HseValidation.SafetyEquipmentCodes,
            PermitVersion = Version
        });
    }

    public void RequestRevision(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderValidation, PermitStatus.AwaitingAreaApproval);
        EnsureReason(reason);
        ClearReviewEvidence();
        MoveTo(PermitStatus.RevisionRequired, "revision_requested", now, new { Reason = reason.Trim() });
    }

    public void EscalateValidation(string actorId, string note, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderValidation);
        EnsureEvidence(actorId, note);
        Touch(now);
        Raise("hse_validation_escalated", new
        {
            EscalatedBy = actorId.Trim(),
            Note = note.Trim(),
            PermitVersion = Version
        });
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderValidation, PermitStatus.AwaitingAreaApproval);
        EnsureReason(reason);
        MoveTo(PermitStatus.Rejected, "permit_rejected", now, new { Reason = reason.Trim() });
    }

    public void ApproveAndIssue(PermitApprovalEvidence approval, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.AwaitingAreaApproval);
        EnsureOtherWorkTypeDetailIsComplete(Draft);
        EnsureEvidence(approval.ActorId, approval.Statement);
        if (HseValidation is null)
        {
            throw new DomainRuleViolationException(
                "permit.validation.incomplete",
                "Validasi HSE wajib selesai sebelum approval penerbitan.");
        }

        if (HseValidation.SafetyEquipmentCodes is not { Count: > 0 })
        {
            throw new DomainRuleViolationException(
                "permit.safety_equipment_required",
                "Pilihan APD/perlengkapan safety Bagian 5 belum dicatat oleh PIC HSE. Minta revisi sebelum menerbitkan PTW.");
        }

        PermitSafetyEquipmentCatalog.NormalizeAndValidate(Draft.PermitClass, HseValidation.SafetyEquipmentCodes);

        if (string.Equals(Draft.SponsorId, approval.ActorId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(HseValidation.ActorId, approval.ActorId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.approval.separation_of_duty",
                "Sponsor atau validator HSE tidak boleh menyetujui dan menerbitkan PTW yang sama.");
        }

        if (approval.AuthorizationId == Guid.Empty
            || string.IsNullOrWhiteSpace(approval.ActorPosition)
            || string.IsNullOrWhiteSpace(approval.PrincipalManagerUserId)
            || string.IsNullOrWhiteSpace(approval.PrincipalPosition)
            || string.IsNullOrWhiteSpace(approval.RuleVersion)
            || string.IsNullOrWhiteSpace(approval.PrintTemplateVersion)
            || string.IsNullOrWhiteSpace(approval.CampaignAssetVersion)
            || approval.Capacity == ApprovalCapacity.ActingForManager && approval.ActingAssignmentId is null)
        {
            throw new DomainRuleViolationException(
                "permit.approval.authorization_evidence_required",
                "Snapshot otorisasi Manager atau pengganti resmi wajib lengkap.");
        }

        Approval = approval with { ApprovedAt = now.ToUniversalTime() };
        MoveTo(PermitStatus.Issued, "permit_issued", now, new
        {
            Approval.ActorId,
            Capacity = Approval.Capacity.ToString(),
            Approval.PrincipalManagerUserId,
            Approval.AuthorizationId,
            Approval.ActingAssignmentId,
            Approval.Statement,
            PermitVersion = Version
        });
    }

    public void Suspend(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued);
        EnsureEvidence(actorId, reason);
        SuspensionReason = reason.Trim();
        Suspension = new PermitSuspensionEvidence(actorId.Trim(), SuspensionReason, now.ToUniversalTime());
        MoveTo(PermitStatus.Suspended, "permit_suspended", now, new
        {
            Suspension.SuspendedBy,
            Suspension.Reason
        });
    }

    public void ResolveSuspension(string actorId, string resolution, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Suspended);
        EnsureEvidence(actorId, resolution);
        if (Suspension is null)
        {
            throw new DomainRuleViolationException(
                "permit.suspension.evidence_missing",
                "Bukti penangguhan tidak tersedia.");
        }

        var previousReason = SuspensionReason;
        Suspension = Suspension with
        {
            ResolvedBy = actorId.Trim(),
            Resolution = resolution.Trim(),
            ResolvedAt = now.ToUniversalTime()
        };
        SuspensionReason = null;
        MoveTo(PermitStatus.Issued, "permit_suspension_resolved", now, new
        {
            Reason = previousReason,
            Suspension.ResolvedBy,
            Suspension.Resolution,
            RequiresHardcopyRevalidation = true
        });
    }

    public void RequestClosure(
        Guid printPackageId,
        IReadOnlyList<Guid> attachmentIds,
        string actorId,
        string completionStatement,
        DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Suspended, PermitStatus.Expired);
        if (RenewalPermitId is not null
            || RenewalRequest?.Status is PermitRenewalReviewStatus.Pending or PermitRenewalReviewStatus.RevisionRequired)
        {
            throw new DomainRuleViolationException(
                "permit.closure.renewal_conflict",
                "Penutupan tidak dapat diajukan selama proses perpanjangan masih aktif.");
        }
        EnsureEvidence(actorId, completionStatement);
        if (!string.Equals(Draft.SponsorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.closure.sponsor_mismatch",
                "Hanya Sponsor PTW yang dapat mengajukan penutupan.");
        }

        if (printPackageId == Guid.Empty || attachmentIds.Count == 0 || attachmentIds.Any(x => x == Guid.Empty))
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_required",
                "Paket cetak dan hardcopy final wajib dipilih sebelum mengajukan penutupan.");
        }

        ClosureRequest = new PermitClosureEvidence(
            printPackageId,
            attachmentIds.Distinct().ToArray(),
            actorId.Trim(),
            completionStatement.Trim(),
            now.ToUniversalTime(),
            1);
        MoveTo(PermitStatus.ClosureRequested, "closure_requested", now, new
        {
            PrintPackageId = printPackageId,
            AttachmentIds = ClosureRequest.AttachmentIds,
            ClosureRequest.RequestedBy,
            ClosureRequest.CompletionStatement,
            PermitVersion = Version
        });
    }

    public void RequestClosureEvidenceReplacement(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.ClosureRequested);
        EnsureEvidence(actorId, reason);
        if (ClosureRequest is null)
        {
            throw new DomainRuleViolationException(
                "permit.closure.request_missing",
                "Permintaan penutupan tidak tersedia.");
        }

        ClosureRequest = ClosureRequest with
        {
            Revision = ClosureRequest.Revision + 1,
            ReplacementReason = reason.Trim()
        };
        Touch(now);
        Raise("closure_evidence_replacement_requested", new
        {
            RequestedBy = actorId.Trim(),
            Reason = reason.Trim(),
            ClosureRequest.Revision
        });
    }

    public void Close(string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.ClosureRequested);
        EnsureEvidence(actorId, statement);
        if (ClosureRequest is null)
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_required",
                "Bukti hardcopy final wajib tersedia sebelum PTW ditutup.");
        }

        ClosureDecision = new PermitClosureDecisionEvidence(actorId.Trim(), statement.Trim(), now.ToUniversalTime());
        MoveTo(PermitStatus.Closed, "permit_closed", now, new
        {
            ClosedBy = actorId.Trim(),
            Statement = statement.Trim(),
            ClosureRequest.PrintPackageId,
            ClosureRequest.AttachmentIds
        });
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.UnderValidation, PermitStatus.RevisionRequired);
        EnsureReason(reason);
        MoveTo(PermitStatus.Cancelled, "permit_cancelled", now, new { Reason = reason.Trim() });
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Issued, PermitStatus.Suspended);
        if (now < Draft.ValidUntil)
        {
            throw new DomainRuleViolationException("permit.not_expired", "PTW belum mencapai akhir masa berlaku.");
        }

        MoveTo(PermitStatus.Expired, "permit_expired", now);
    }

    public IReadOnlyList<DomainEvent> DequeueEvents()
    {
        var result = _events.ToArray();
        _events.Clear();
        return result;
    }

    private Permit(Guid id, PermitDraft draft, DateTimeOffset createdAt, bool draftIsNormalized)
    {
        Id = id;
        Draft = draftIsNormalized ? draft : NormalizeAndValidate(draft);
        Status = PermitStatus.Draft;
        Version = 1;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    private static PermitDraft NormalizeAndValidate(
        PermitDraft value,
        bool allowLegacyIncompleteDraft = false)
    {
        if (string.IsNullOrWhiteSpace(value.Title) || string.IsNullOrWhiteSpace(value.Description)
            || string.IsNullOrWhiteSpace(value.LocationId) || string.IsNullOrWhiteSpace(value.SponsorId)
            || string.IsNullOrWhiteSpace(value.PerformingAuthority) || string.IsNullOrWhiteSpace(value.Company))
        {
            throw new DomainRuleViolationException(
                "permit.required_fields",
                "Data pekerjaan, lokasi, Sponsor, pelaksana, dan perusahaan wajib diisi.");
        }

        var submitterType = (value.SubmitterType ?? string.Empty).Trim().ToUpperInvariant();
        if (submitterType is not ("CONTRACTOR" or "USER_SPONSOR"))
        {
            throw new DomainRuleViolationException(
                "permit.submitter_type_invalid",
                "Tipe pengaju harus CONTRACTOR atau USER_SPONSOR.");
        }

        var from = value.ValidFrom.ToUniversalTime();
        var until = value.ValidUntil.ToUniversalTime();
        if (from >= until)
        {
            throw new DomainRuleViolationException("permit.invalid_validity", "Waktu mulai harus lebih awal daripada waktu selesai.");
        }

        if (until - from > TimeSpan.FromDays(7))
        {
            throw new DomainRuleViolationException(
                "permit.validity_exceeds_seven_days",
                "Masa berlaku PTW maksimum tujuh hari.");
        }

        var workTypeCodes = PermitWorkTypeCatalog.NormalizeAndValidate(
            value.PermitClass,
            value.WorkTypeCodes,
            value.WorkTypeCode);
        var otherWorkTypeDescription = NormalizeAndValidateOtherWorkTypeDescription(
            value.PermitClass,
            workTypeCodes,
            value.OtherWorkTypeDescription,
            allowLegacyIncompleteDraft);
        var requiredDocumentCodes = PermitSupportingDocumentCatalog.NormalizeAndValidate(
            value.RequiredDocumentCodes,
            allowMissingRequired: allowLegacyIncompleteDraft);
        var headerClassificationCodes = PermitHeaderClassificationCatalog.NormalizeAndValidate(
            value.PermitClass,
            value.HeaderClassificationCodes,
            allowMissing: true);
        var riskLevel = value.PermitClass == PermitClass.ColdWork
            ? ResolveColdWorkLegacyRisk(value.RiskLevel, headerClassificationCodes)
            : value.RiskLevel;

        return value with
        {
            Title = value.Title.Trim(),
            Description = value.Description.Trim(),
            LocationId = value.LocationId.Trim(),
            SponsorId = value.SponsorId.Trim(),
            PerformingAuthority = value.PerformingAuthority.Trim(),
            Company = value.Company.Trim(),
            SubmitterType = submitterType,
            WorkTypeCode = workTypeCodes[0],
            WorkTypeCodes = workTypeCodes,
            OtherWorkTypeDescription = otherWorkTypeDescription,
            HeaderClassificationCodes = headerClassificationCodes,
            RiskLevel = riskLevel,
            RequiredDocumentCodes = requiredDocumentCodes,
            EquipmentTag = NormalizeOptional(value.EquipmentTag),
            EquipmentName = NormalizeOptionalWithMaxLength(
                value.EquipmentName,
                100,
                "permit.equipment_name_too_long",
                "Nama equipment maksimum 100 karakter."),
            WorkOrderNumber = NormalizeOptionalWithMaxLength(
                value.WorkOrderNumber,
                60,
                "permit.work_order_number_too_long",
                "Nomor work order maksimum 60 karakter."),
            AdditionalHazardReference = NormalizeOptionalWithMaxLength(
                value.AdditionalHazardReference,
                160,
                "permit.additional_hazard_reference_too_long",
                "Referensi bahaya tambahan maksimum 160 karakter."),
            PlantArea = NormalizeOptional(value.PlantArea),
            SimopsDeclaration = NormalizeOptional(value.SimopsDeclaration),
            SafetyEquipmentCodes = NormalizeCodes(value.SafetyEquipmentCodes),
            IsolationPrecautionCodes = NormalizeCodes(value.IsolationPrecautionCodes),
            JsaDocumentNumber = NormalizeOptional(value.JsaDocumentNumber),
            JsaRevision = NormalizeOptional(value.JsaRevision),
            JsaDate = value.JsaDate?.ToUniversalTime(),
            ValidFrom = from,
            ValidUntil = until
        };
    }

    private static string? NormalizeAndValidateOtherWorkTypeDescription(
        PermitClass permitClass,
        IReadOnlyCollection<string> workTypeCodes,
        string? description,
        bool allowMissingRequiredDetail = false)
    {
        var otherWorkTypeDescription = NormalizeOptional(description);
        var requiresOtherDetail = PermitWorkTypeCatalog.RequiresDetail(permitClass, workTypeCodes);
        if (requiresOtherDetail && otherWorkTypeDescription is null && !allowMissingRequiredDetail)
        {
            throw new DomainRuleViolationException(
                "permit.work_type_other_detail_required",
                "Jelaskan jenis pekerjaan saat pilihan Lain-lain dipilih.");
        }

        if (!requiresOtherDetail && otherWorkTypeDescription is not null)
        {
            throw new DomainRuleViolationException(
                "permit.work_type_other_detail_without_selection",
                "Detail jenis pekerjaan Lain-lain hanya boleh diisi ketika pilihannya dipilih.");
        }

        if (otherWorkTypeDescription?.Length > 80)
        {
            throw new DomainRuleViolationException(
                "permit.work_type_other_detail_too_long",
                "Detail jenis pekerjaan Lain-lain maksimum 80 karakter agar sesuai ruang pada formulir resmi.");
        }

        return otherWorkTypeDescription;
    }

    private static void EnsureOtherWorkTypeDetailIsComplete(PermitDraft draft)
    {
        var workTypeCodes = PermitWorkTypeCatalog.NormalizeAndValidate(
            draft.PermitClass,
            draft.WorkTypeCodes,
            draft.WorkTypeCode);
        _ = NormalizeAndValidateOtherWorkTypeDescription(
            draft.PermitClass,
            workTypeCodes,
            draft.OtherWorkTypeDescription);
    }

    private static void EnsureSupportingDocumentSelectionIsComplete(PermitDraft draft) =>
        _ = PermitSupportingDocumentCatalog.NormalizeAndValidate(draft.RequiredDocumentCodes);

    private static void EnsureHeaderClassificationIsComplete(PermitDraft draft) =>
        _ = PermitHeaderClassificationCatalog.NormalizeAndValidate(
            draft.PermitClass,
            draft.HeaderClassificationCodes);

    private static RiskLevel ResolveColdWorkLegacyRisk(
        RiskLevel current,
        IReadOnlyCollection<string> headerClassificationCodes)
    {
        if (headerClassificationCodes.Contains("COLD_LOW_RISK", StringComparer.OrdinalIgnoreCase))
        {
            return RiskLevel.Low;
        }

        if (headerClassificationCodes.Contains("COLD_HIGH_RISK", StringComparer.OrdinalIgnoreCase))
        {
            return RiskLevel.High;
        }

        return current;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalWithMaxLength(
        string? value,
        int maxLength,
        string code,
        string message)
    {
        var normalized = NormalizeOptional(value);
        if (normalized?.Length > maxLength)
        {
            throw new DomainRuleViolationException(code, message);
        }

        return normalized;
    }

    private static string[] NormalizeCodes(IReadOnlyList<string>? values) =>
        values?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private static void EnsureSafetyEquipmentIsAssignedByHse(PermitDraft draft)
    {
        if (draft.SafetyEquipmentCodes is { Count: > 0 })
        {
            throw new DomainRuleViolationException(
                "permit.safety_equipment.hse_owned",
                "APD/perlengkapan safety hanya dapat ditetapkan oleh PIC HSE saat validasi.");
        }
    }

    private void EnsurePendingRenewalRequest()
    {
        if (RenewalRequest?.Status != PermitRenewalReviewStatus.Pending)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.request_missing",
                "Permintaan perpanjangan yang menunggu review tidak tersedia.");
        }
    }

    private void MoveTo(PermitStatus status, string eventType, DateTimeOffset now, object? payload = null)
    {
        var from = Status;
        Status = status;
        Touch(now);
        Raise(eventType, payload ?? new { From = from.ToString(), To = status.ToString() });
    }

    private void Raise(string type, object payload) =>
        _events.Add(new DomainEvent(Guid.CreateVersion7(), Id, type, UpdatedAt, payload));

    private void Touch(DateTimeOffset now) => UpdatedAt = now.ToUniversalTime();

    private void EnsureStatus(params PermitStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainRuleViolationException("permit.invalid_transition", $"Aksi tidak diizinkan dari status {Status}.");
        }
    }

    private static void EnsureReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleViolationException("permit.reason_required", "Alasan wajib diisi.");
        }
    }

    private static void EnsureEvidence(string actorId, string statement)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(statement))
        {
            throw new DomainRuleViolationException(
                "permit.evidence_required",
                "Identitas aktor dan pernyataan keputusan wajib dicatat.");
        }
    }

    private void ClearReviewEvidence()
    {
        HseValidation = null;
        Approval = null;
        Draft = Draft with { SafetyEquipmentCodes = [] };
    }
}
