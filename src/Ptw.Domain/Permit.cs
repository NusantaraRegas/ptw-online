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
        ChangeSequence = 1;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }
    public string? PermitNumber { get; private set; }
    public PermitStatus Status { get; private set; }
    /// <summary>The business revision of the PTW document.</summary>
    public int Version { get; private set; }
    /// <summary>
    /// Legacy aggregate mutation sequence retained for persistence compatibility and data migration.
    /// Concurrency itself is enforced by the persisted row-version/ETag.
    /// </summary>
    public int ChangeSequence { get; private set; }
    public PermitDraft Draft { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? RenewedFromPermitId { get; private set; }
    public Guid? RenewalPermitId { get; private set; }
    public string? SuspensionReason { get; private set; }
    public PermitValidationEvidence? HseValidation { get; private set; }
    public AreaOperationsReviewEvidence? AreaOperationsReview { get; private set; }
    public PermitApprovalEvidence? Approval { get; private set; }
    public PermitSuspensionEvidence? Suspension { get; private set; }
    public PermitClosureEvidence? ClosureRequest { get; private set; }
    public PermitClosureDecisionEvidence? ClosureDecision { get; private set; }
    public PermitRenewalRequestEvidence? RenewalRequest { get; private set; }
    /// <summary>Bagian 5 choice saved by the PIC HSE; survives revision cycles until validation.</summary>
    public HseSafetyEquipmentSelection? SafetyEquipmentSelection { get; private set; }
    /// <summary>Set only for drafts initiated by a Contractor that a Sponsor must approve first.</summary>
    public PermitInitiation? Initiation { get; private set; }
    public IReadOnlyList<DomainEvent> Events => _events;

    public static Permit CreateDraft(PermitDraft draft, DateTimeOffset now, PermitInitiation? initiation = null)
    {
        EnsureSafetyEquipmentIsAssignedByHse(draft);
        var permit = new Permit(Guid.CreateVersion7(), draft, now);
        if (initiation is not null)
        {
            if (string.IsNullOrWhiteSpace(initiation.RequesterId))
            {
                throw new DomainRuleViolationException(
                    "permit.initiation.requester_required",
                    "Identitas Pemohon pengisi PTW awal wajib tersedia.");
            }

            permit.Initiation = initiation with
            {
                RequesterId = initiation.RequesterId.Trim(),
                Status = PermitInitiationStatus.Drafting,
                CreatedAt = now.ToUniversalTime(),
                RequesterName = NormalizeOptional(initiation.RequesterName)
            };
        }

        permit.Raise("permit_draft_created", new
        {
            permit.Version,
            InitiatedBy = permit.Initiation?.RequesterId,
            InitiatedByName = permit.Initiation?.RequesterName
        });
        return permit;
    }

    /// <summary>Contractor hands the completed initial PTW to the Sponsor for approval.</summary>
    public void SubmitInitiationToSponsor(string actorId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft);
        var initiation = EnsureInitiationOwnedBy(actorId);
        if (initiation.Status is not (PermitInitiationStatus.Drafting or PermitInitiationStatus.Returned))
        {
            throw new DomainRuleViolationException(
                "permit.initiation.invalid_state",
                "PTW awal sudah diajukan kepada Sponsor atau sudah disetujui.");
        }

        EnsureHeaderClassificationIsComplete(Draft);
        EnsureOtherWorkTypeDetailIsComplete(Draft);
        EnsureSupportingDocumentSelectionIsComplete(Draft);
        Initiation = initiation with
        {
            Status = PermitInitiationStatus.AwaitingSponsorApproval,
            SubmittedToSponsorAt = now.ToUniversalTime(),
            SponsorStatement = null,
            DecidedAt = null
        };
        ChangeSequence++;
        Touch(now);
        Raise("permit_initial_submitted", new
        {
            RequesterId = Initiation.RequesterId,
            SponsorId = Draft.SponsorId,
            PermitVersion = Version
        });
    }

    public void ApproveInitiation(string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft);
        EnsureEvidence(actorId, statement);
        var initiation = EnsureInitiationAwaitingSponsor(actorId);
        Initiation = initiation with
        {
            Status = PermitInitiationStatus.Approved,
            SponsorStatement = statement.Trim(),
            DecidedAt = now.ToUniversalTime()
        };
        ChangeSequence++;
        Touch(now);
        Raise("permit_initial_approved", new
        {
            ApprovedBy = actorId.Trim(),
            Statement = statement.Trim(),
            RequesterId = Initiation.RequesterId,
            PermitVersion = Version
        });
    }

    public void ReturnInitiation(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft);
        EnsureEvidence(actorId, reason);
        var initiation = EnsureInitiationAwaitingSponsor(actorId);
        Initiation = initiation with
        {
            Status = PermitInitiationStatus.Returned,
            SponsorStatement = reason.Trim(),
            DecidedAt = now.ToUniversalTime()
        };
        ChangeSequence++;
        Touch(now);
        Raise("permit_initial_returned", new
        {
            ReturnedBy = actorId.Trim(),
            Reason = reason.Trim(),
            RequesterId = Initiation.RequesterId,
            PermitVersion = Version
        });
    }

    /// <summary>Sponsor rejects the Contractor's initial PTW outright; the draft becomes terminal REJECTED.</summary>
    public void RejectInitiation(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft);
        EnsureEvidence(actorId, reason);
        var initiation = EnsureInitiationAwaitingSponsor(actorId);
        Initiation = initiation with
        {
            Status = PermitInitiationStatus.Rejected,
            SponsorStatement = reason.Trim(),
            DecidedAt = now.ToUniversalTime()
        };
        MoveTo(PermitStatus.Rejected, "permit_initial_rejected", now, new
        {
            RejectedBy = actorId.Trim(),
            Reason = reason.Trim(),
            RequesterId = Initiation.RequesterId,
            PermitVersion = Version
        });
    }

    /// <summary>
    /// Stores the PIC HSE Bagian 5 choice without deciding the validation. The selection is reused by
    /// <see cref="ValidateSubmission"/> and is deliberately not cleared by revision requests.
    /// </summary>
    public void SaveSafetyEquipmentSelection(
        string actorId,
        IReadOnlyList<string> safetyEquipmentCodes,
        DateTimeOffset now,
        string? actorName = null)
    {
        EnsureStatus(PermitStatus.UnderValidation);
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new DomainRuleViolationException("permit.evidence_required", "Identitas aktor wajib dicatat.");
        }

        EnsureNotSponsor(
            actorId,
            "permit.validation.self_validation_forbidden",
            "Sponsor PTW tidak boleh menetapkan APD pada pengajuannya sendiri.");
        var selected = PermitSafetyEquipmentCatalog.NormalizeAndValidate(Draft.PermitClass, safetyEquipmentCodes);
        SafetyEquipmentSelection = new HseSafetyEquipmentSelection(
            actorId.Trim(),
            selected,
            now.ToUniversalTime(),
            NormalizeOptional(actorName));
        ChangeSequence++;
        Touch(now);
        Raise("hse_safety_equipment_saved", new
        {
            SafetyEquipmentSelection.ActorId,
            SafetyEquipmentSelection.ActorName,
            SafetyEquipmentSelection.SafetyEquipmentCodes,
            PermitVersion = Version
        });
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
        Guid? renewalPermitId = null,
        AreaOperationsReviewEvidence? areaOperationsReview = null,
        int? changeSequence = null,
        HseSafetyEquipmentSelection? safetyEquipmentSelection = null,
        PermitInitiation? initiation = null) =>
        new(id, NormalizeAndValidate(draft, allowLegacyIncompleteDraft: true), createdAt, draftIsNormalized: true)
        {
            PermitNumber = permitNumber,
            Status = status,
            Version = version,
            ChangeSequence = changeSequence ?? version,
            UpdatedAt = updatedAt.ToUniversalTime(),
            SuspensionReason = suspensionReason,
            HseValidation = hseValidation,
            AreaOperationsReview = areaOperationsReview,
            Approval = approval,
            Suspension = suspension,
            ClosureRequest = closureRequest,
            ClosureDecision = closureDecision,
            RenewalRequest = renewalRequest,
            RenewedFromPermitId = renewedFromPermitId,
            RenewalPermitId = renewalPermitId,
            SafetyEquipmentSelection = safetyEquipmentSelection,
            Initiation = initiation
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
        ChangeSequence++;
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
            PermitVersion = Version
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
        ChangeSequence++;
        Touch(now);
        Raise("permit_draft_updated", new { Version });
    }

    public void AddAttachment(Guid attachmentId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        ChangeSequence++;
        Touch(now);
        Raise("permit_attachment_added", new { AttachmentId = attachmentId, Version });
    }

    public void AddSignedFieldCopy(Guid attachmentId, Guid printPackageId, DateTimeOffset now)
    {
        EnsureStatus(
            PermitStatus.Issued,
            PermitStatus.Suspended,
            PermitStatus.Expired,
            PermitStatus.ClosureRequested);
        if (attachmentId == Guid.Empty || printPackageId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "permit.closure.attachment_reference_required",
                "Attachment dan PrintPackage wajib tersedia untuk signed field copy.");
        }

        if (Status == PermitStatus.ClosureRequested)
        {
            if (ClosureRequest is null || string.IsNullOrWhiteSpace(ClosureRequest.ReplacementReason))
            {
                throw new DomainRuleViolationException(
                    "permit.closure.evidence_replacement_not_requested",
                    "Salinan lapangan baru hanya dapat diunggah setelah Pemilik Wilayah meminta tindak lanjut.");
            }

            if (ClosureRequest.PrintPackageId != printPackageId)
            {
                throw new DomainRuleViolationException(
                    "permit.closure.print_package_changed",
                    "Salinan lapangan pengganti harus menggunakan paket cetak yang sama dengan permintaan penutupan.");
            }
        }

        if (RenewalRequest?.Status == PermitRenewalReviewStatus.Pending)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.review_pending",
                "Lampiran tidak dapat diubah selama permintaan perpanjangan sedang ditinjau.");
        }

        ChangeSequence++;
        Touch(now);
        Raise("signed_field_copy_uploaded", new { AttachmentId = attachmentId, PrintPackageId = printPackageId, Version });
    }

    public void RemoveAttachment(Guid attachmentId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        ChangeSequence++;
        Touch(now);
        Raise("permit_attachment_removed", new { AttachmentId = attachmentId, Version });
    }

    public void Submit(string permitNumber, SubmissionReadiness readiness, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        var isResubmission = Status == PermitStatus.RevisionRequired;
        EnsureHeaderClassificationIsComplete(Draft);
        EnsureOtherWorkTypeDetailIsComplete(Draft);
        EnsureSupportingDocumentSelectionIsComplete(Draft);
        if (!readiness.IsReady)
        {
            throw new DomainRuleViolationException(
                "permit.submit.requirements_incomplete",
                "PTW belum memenuhi seluruh persyaratan submit.");
        }

        if (Initiation is not null && Initiation.Status != PermitInitiationStatus.Approved)
        {
            throw new DomainRuleViolationException(
                "permit.submit.initial_approval_required",
                "PTW awal dari Pemohon harus disetujui Sponsor sebelum diajukan kepada PIC HSE.");
        }

        if (isResubmission && !readiness.RevisionSelfCheckConfirmed)
        {
            throw new DomainRuleViolationException(
                "permit.submit.revision_self_check_required",
                "Sponsor wajib mengonfirmasi bahwa seluruh permintaan revisi telah dilaksanakan sebelum mengajukan ulang.");
        }

        if (string.IsNullOrWhiteSpace(permitNumber))
        {
            throw new DomainRuleViolationException("permit.number.required", "Nomor PTW resmi wajib tersedia saat submit.");
        }

        PermitNumber ??= permitNumber.Trim();
        ClearReviewEvidence();
        if (isResubmission)
        {
            // A revision starts a new review cycle. Keep the cancelled workflow task
            // as immutable history and bind the fresh task to a new permit version.
            Version++;
            ChangeSequence++;
        }

        var from = Status;
        MoveTo(PermitStatus.UnderValidation, "permit_submitted", now, new
        {
            From = from.ToString(),
            To = PermitStatus.UnderValidation.ToString(),
            PermitNumber,
            PermitVersion = Version,
            Resubmission = isResubmission,
            readiness.RevisionSelfCheckConfirmed
        });
    }

    public void ValidateSubmission(
        string actorId,
        string statement,
        IReadOnlyList<string>? safetyEquipmentCodes,
        DateTimeOffset now,
        string? actorName = null,
        bool revisionSelfCheckConfirmed = false)
    {
        EnsureStatus(PermitStatus.UnderValidation);
        EnsureOtherWorkTypeDetailIsComplete(Draft);
        EnsureEvidence(actorId, statement);
        EnsureNotSponsor(
            actorId,
            "permit.validation.self_validation_forbidden",
            "Sponsor PTW tidak boleh memvalidasi pengajuannya sendiri.");
        if (Version > 1 && !revisionSelfCheckConfirmed)
        {
            throw new DomainRuleViolationException(
                "permit.validation.revision_self_check_required",
                "PIC HSE wajib mengonfirmasi bahwa revisi telah dilaksanakan sesuai permintaan sebelum memvalidasi ulang.");
        }

        // A decision without an explicit choice reuses the selection saved through the separate
        // "Simpan Pilihan APD" command, so a revision cycle never discards the HSE's work.
        var requestedCodes = safetyEquipmentCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).ToArray() ?? [];
        var selectedSafetyEquipment = PermitSafetyEquipmentCatalog.NormalizeAndValidate(
            Draft.PermitClass,
            requestedCodes.Length > 0 ? requestedCodes : SafetyEquipmentSelection?.SafetyEquipmentCodes);
        HseValidation = new PermitValidationEvidence(
            actorId.Trim(),
            statement.Trim(),
            now.ToUniversalTime(),
            selectedSafetyEquipment,
            NormalizeOptional(actorName),
            revisionSelfCheckConfirmed);
        SafetyEquipmentSelection = new HseSafetyEquipmentSelection(
            actorId.Trim(),
            selectedSafetyEquipment,
            now.ToUniversalTime(),
            NormalizeOptional(actorName));
        MoveTo(PermitStatus.AwaitingAreaApproval, "hse_validation_completed", now, new
        {
            HseValidation.ActorId,
            HseValidation.ActorName,
            HseValidation.Statement,
            HseValidation.SafetyEquipmentCodes,
            HseValidation.RevisionSelfCheckConfirmed,
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

        if (AreaOperationsReview is null)
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.review_required",
                "Verifikasi kondisi operasi Bagian 7 oleh SO/Officer Pemilik Wilayah wajib selesai sebelum penerbitan.");
        }

        if (HseValidation.SafetyEquipmentCodes is not { Count: > 0 })
        {
            throw new DomainRuleViolationException(
                "permit.safety_equipment_required",
                "Pilihan APD/perlengkapan safety Bagian 5 belum dicatat oleh PIC HSE. Minta revisi sebelum menerbitkan PTW.");
        }

        PermitSafetyEquipmentCatalog.NormalizeAndValidate(Draft.PermitClass, HseValidation.SafetyEquipmentCodes);

        if (string.Equals(Draft.SponsorId, approval.ActorId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(HseValidation.ActorId, approval.ActorId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(AreaOperationsReview.ActorId, approval.ActorId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.approval.separation_of_duty",
                "Sponsor, validator HSE, atau reviewer SO/Officer tidak boleh menyetujui dan menerbitkan PTW yang sama.");
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

    public void ReviewAreaOperations(AreaOperationsReviewEvidence review, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.AwaitingAreaApproval);
        EnsureEvidence(review.ActorId, review.Statement);
        if (HseValidation is null)
        {
            throw new DomainRuleViolationException(
                "permit.validation.incomplete",
                "Validasi HSE wajib selesai sebelum verifikasi kondisi operasi Bagian 7.");
        }

        if (AreaOperationsReview is not null)
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.already_reviewed",
                "Kondisi operasi Bagian 7 sudah diverifikasi untuk versi PTW ini.");
        }

        if (string.Equals(Draft.SponsorId, review.ActorId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(HseValidation.ActorId, review.ActorId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.separation_of_duty",
                "Sponsor atau validator HSE tidak boleh menjadi reviewer SO/Officer Pemilik Wilayah pada PTW yang sama.");
        }

        if (review.AuthorizationId == Guid.Empty
            || string.IsNullOrWhiteSpace(review.ActorName)
            || string.IsNullOrWhiteSpace(review.ActorPosition))
        {
            throw new DomainRuleViolationException(
                "permit.area_operations.authorization_evidence_required",
                "Snapshot otorisasi SO/Officer Pemilik Wilayah wajib lengkap.");
        }

        var conditions = PermitOperationalConditionCatalog.NormalizeAndValidate(
            review.ConditionCodes,
            review.OtherConditionDetail);
        AreaOperationsReview = review with
        {
            ActorId = review.ActorId.Trim(),
            ActorName = review.ActorName.Trim(),
            ActorPosition = review.ActorPosition.Trim(),
            ConditionCodes = conditions,
            OtherConditionDetail = string.IsNullOrWhiteSpace(review.OtherConditionDetail)
                ? null
                : review.OtherConditionDetail.Trim(),
            Statement = review.Statement.Trim(),
            ReviewedAt = now.ToUniversalTime()
        };
        Touch(now);
        Raise("area_operations_review_completed", new
        {
            AreaOperationsReview.ActorId,
            AreaOperationsReview.ActorPosition,
            AreaOperationsReview.AuthorizationId,
            AreaOperationsReview.ConditionCodes,
            AreaOperationsReview.OtherConditionDetail,
            AreaOperationsReview.Statement,
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
        PermitWorkCompletionDeclaration completion,
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
            1,
            Completion: NormalizeCompletion(completion));
        MoveTo(PermitStatus.ClosureRequested, "closure_requested", now, new
        {
            PrintPackageId = printPackageId,
            AttachmentIds = ClosureRequest.AttachmentIds,
            ClosureRequest.RequestedBy,
            ClosureRequest.CompletionStatement,
            Section9 = ClosureRequest.Completion,
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

        if (!string.IsNullOrWhiteSpace(ClosureRequest.ReplacementReason))
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_replacement_pending",
                "Tindak lanjut Sponsor masih menunggu pengajuan ulang.");
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

    public void ResubmitClosure(
        Guid printPackageId,
        IReadOnlyList<Guid> attachmentIds,
        string actorId,
        string completionStatement,
        PermitWorkCompletionDeclaration completion,
        DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.ClosureRequested);
        EnsureEvidence(actorId, completionStatement);
        if (!string.Equals(Draft.SponsorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.closure.sponsor_mismatch",
                "Hanya Sponsor PTW yang dapat mengajukan ulang penutupan.");
        }

        if (ClosureRequest is null || string.IsNullOrWhiteSpace(ClosureRequest.ReplacementReason))
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_replacement_not_requested",
                "Pengajuan ulang hanya dapat dilakukan setelah Pemilik Wilayah meminta tindak lanjut.");
        }

        if (printPackageId != ClosureRequest.PrintPackageId)
        {
            throw new DomainRuleViolationException(
                "permit.closure.print_package_changed",
                "Pengajuan ulang harus menggunakan paket cetak yang sama dengan permintaan penutupan awal.");
        }

        if (attachmentIds.Count == 0 || attachmentIds.Any(x => x == Guid.Empty))
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_required",
                "Hardcopy terbaru wajib dipilih sebelum mengajukan ulang penutupan.");
        }

        var replacementAttachmentIds = attachmentIds.Distinct().ToArray();
        if (replacementAttachmentIds.Length == ClosureRequest.AttachmentIds.Count
            && replacementAttachmentIds.ToHashSet().SetEquals(ClosureRequest.AttachmentIds))
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_not_replaced",
                "Pilih salinan lapangan terbaru yang berbeda dari evidence sebelumnya.");
        }

        ClosureRequest = ClosureRequest with
        {
            AttachmentIds = replacementAttachmentIds,
            RequestedBy = actorId.Trim(),
            CompletionStatement = completionStatement.Trim(),
            RequestedAt = now.ToUniversalTime(),
            ReplacementReason = null,
            Completion = NormalizeCompletion(completion)
        };
        Touch(now);
        Raise("closure_resubmitted", new
        {
            ClosureRequest.PrintPackageId,
            ClosureRequest.AttachmentIds,
            ClosureRequest.RequestedBy,
            ClosureRequest.CompletionStatement,
            ClosureRequest.Revision,
            Section9 = ClosureRequest.Completion,
            PermitVersion = Version
        });
    }

    public void Close(
        string actorId,
        string statement,
        string officerName,
        bool workAreaInspectedAndClean,
        bool workCompleted,
        bool managerAgreesWorkCompleted,
        bool inhibitedSystemsRestored,
        bool areaHandedBackAndSafeguardsRestored,
        bool evidenceReadable,
        DateTimeOffset now,
        string? incompleteWorkStatus = null)
    {
        EnsureStatus(PermitStatus.ClosureRequested);
        EnsureEvidence(actorId, statement);
        if (ClosureRequest is null)
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_required",
                "Bukti hardcopy final wajib tersedia sebelum PTW ditutup.");
        }

        if (!string.IsNullOrWhiteSpace(ClosureRequest.ReplacementReason))
        {
            throw new DomainRuleViolationException(
                "permit.closure.evidence_replacement_pending",
                "PTW belum dapat ditutup karena tindak lanjut Sponsor masih menunggu pengajuan ulang.");
        }

        if (string.IsNullOrWhiteSpace(officerName))
        {
            throw new DomainRuleViolationException(
                "permit.closure.officer_required",
                "Nama Officer yang melakukan inspeksi wajib dicatat.");
        }

        if (officerName.Trim().Length > 100)
        {
            throw new DomainRuleViolationException(
                "permit.closure.officer_too_long",
                "Nama Officer maksimum 100 karakter.");
        }

        if (!workAreaInspectedAndClean
            || !inhibitedSystemsRestored
            || !areaHandedBackAndSafeguardsRestored
            || !evidenceReadable)
        {
            throw new DomainRuleViolationException(
                "permit.closure.verification_incomplete",
                "Seluruh inspeksi, persetujuan Pemilik Wilayah, handback, dan keterbacaan hardcopy wajib dikonfirmasi sebelum PTW ditutup.");
        }

        // Bagian 9/10 pairing: the Sponsor's declaration decides which Bagian 10 statement applies.
        // Closures recorded before the section moved off the hardcopy still require the completed path.
        var sponsorDeclaredCompleted = ClosureRequest.Completion?.WorkCompleted ?? true;
        var normalizedIncompleteStatus = NormalizeOptional(incompleteWorkStatus);
        if (sponsorDeclaredCompleted)
        {
            if (!workCompleted || !managerAgreesWorkCompleted)
            {
                throw new DomainRuleViolationException(
                    "permit.closure.verification_incomplete",
                    "Sponsor menyatakan pekerjaan selesai; Pemilik Wilayah wajib mengonfirmasi penyelesaian sebelum PTW ditutup.");
            }

            normalizedIncompleteStatus = null;
        }
        else
        {
            if (workCompleted || managerAgreesWorkCompleted)
            {
                throw new DomainRuleViolationException(
                    "permit.closure.completion_mismatch",
                    "Sponsor menyatakan pekerjaan belum selesai; Pemilik Wilayah tidak dapat menyatakan pekerjaan selesai.");
            }

            if (normalizedIncompleteStatus is null)
            {
                throw new DomainRuleViolationException(
                    "permit.closure.incomplete_status_required",
                    "Status pekerjaan yang belum selesai wajib dicatat oleh Pemilik Wilayah (Bagian 10).");
            }

            if (normalizedIncompleteStatus.Length > 800)
            {
                throw new DomainRuleViolationException(
                    "permit.closure.incomplete_status_too_long",
                    "Status pekerjaan yang belum selesai maksimum 800 karakter.");
            }
        }

        ClosureDecision = new PermitClosureDecisionEvidence(
            actorId.Trim(),
            statement.Trim(),
            now.ToUniversalTime(),
            officerName.Trim(),
            workAreaInspectedAndClean,
            workCompleted,
            managerAgreesWorkCompleted,
            inhibitedSystemsRestored,
            areaHandedBackAndSafeguardsRestored,
            evidenceReadable,
            normalizedIncompleteStatus);
        MoveTo(PermitStatus.Closed, "permit_closed", now, new
        {
            ClosedBy = actorId.Trim(),
            Statement = statement.Trim(),
            ClosureDecision.OfficerName,
            ClosureDecision.WorkAreaInspectedAndClean,
            ClosureDecision.WorkCompleted,
            ClosureDecision.ManagerAgreesWorkCompleted,
            ClosureDecision.InhibitedSystemsRestored,
            ClosureDecision.AreaHandedBackAndSafeguardsRestored,
            ClosureDecision.EvidenceReadable,
            ClosureDecision.IncompleteWorkStatus,
            Section9 = ClosureRequest.Completion,
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
        ChangeSequence = 1;
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

    private static PermitWorkCompletionDeclaration NormalizeCompletion(PermitWorkCompletionDeclaration completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var performingAuthorityName = NormalizeOptionalWithMaxLength(
            completion.PerformingAuthorityName,
            100,
            "permit.closure.performing_authority_too_long",
            "Nama Pelaksana Pekerjaan maksimum 100 karakter.");
        var performingAuthorityBadge = NormalizeOptionalWithMaxLength(
            completion.PerformingAuthorityBadgeNumber,
            40,
            "permit.closure.badge_too_long",
            "No. ID Badge maksimum 40 karakter.");
        var sponsorBadge = NormalizeOptionalWithMaxLength(
            completion.SponsorBadgeNumber,
            40,
            "permit.closure.badge_too_long",
            "No. ID Badge maksimum 40 karakter.");
        if (performingAuthorityName is null || performingAuthorityBadge is null || sponsorBadge is null)
        {
            throw new DomainRuleViolationException(
                "permit.closure.section9_identity_required",
                "Nama dan No. ID Badge Pelaksana Pekerjaan serta No. ID Badge Sponsor wajib diisi pada Bagian 9.");
        }

        var explanation = NormalizeOptionalWithMaxLength(
            completion.Explanation,
            800,
            "permit.closure.explanation_too_long",
            "Penjelasan Bagian 9 maksimum 800 karakter.");
        if ((!completion.WorkCompleted || !completion.EquipmentAndMaterialsRemoved) && explanation is null)
        {
            throw new DomainRuleViolationException(
                "permit.closure.explanation_required",
                "Penjelasan wajib diisi bila pekerjaan belum selesai atau peralatan/material belum dikeluarkan.");
        }

        return new PermitWorkCompletionDeclaration(
            completion.WorkCompleted,
            completion.EquipmentAndMaterialsRemoved,
            explanation,
            performingAuthorityName,
            performingAuthorityBadge,
            sponsorBadge);
    }

    private PermitInitiation EnsureInitiationOwnedBy(string actorId)
    {
        if (Initiation is null)
        {
            throw new DomainRuleViolationException(
                "permit.initiation.not_contractor_initiated",
                "PTW ini tidak diinisiasi oleh Pemohon.");
        }

        if (!string.Equals(Initiation.RequesterId, actorId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.initiation.requester_mismatch",
                "Hanya Pemohon pengisi PTW awal yang dapat mengajukannya kepada Sponsor.");
        }

        return Initiation;
    }

    private PermitInitiation EnsureInitiationAwaitingSponsor(string actorId)
    {
        if (Initiation is null)
        {
            throw new DomainRuleViolationException(
                "permit.initiation.not_contractor_initiated",
                "PTW ini tidak diinisiasi oleh Pemohon.");
        }

        if (Initiation.Status != PermitInitiationStatus.AwaitingSponsorApproval)
        {
            throw new DomainRuleViolationException(
                "permit.initiation.invalid_state",
                "PTW awal belum diajukan kepada Sponsor atau sudah diputuskan.");
        }

        if (!string.Equals(Draft.SponsorId, actorId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.initiation.sponsor_mismatch",
                "Hanya Sponsor yang dipilih Pemohon yang dapat memutuskan PTW awal ini.");
        }

        return Initiation;
    }

    private void EnsureNotSponsor(string actorId, string code, string message)
    {
        if (string.Equals(Draft.SponsorId, actorId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(code, message);
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
        AreaOperationsReview = null;
        Approval = null;
        Draft = Draft with { SafetyEquipmentCodes = [] };
    }
}
