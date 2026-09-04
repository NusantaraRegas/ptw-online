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
    public Guid? ActiveWorkPeriodId { get; private set; }
    public Guid? RenewedFromPermitId { get; private set; }
    public Guid? RenewalPermitId { get; private set; }
    public string? SuspensionReason { get; private set; }
    public PermitValidationEvidence? HsseValidation { get; private set; }
    public PermitValidationEvidence? GasDistributionValidation { get; private set; }
    public PermitApprovalEvidence? Approval { get; private set; }
    public PermitSuspensionEvidence? Suspension { get; private set; }
    public PermitCompletionEvidence? SponsorCompletion { get; private set; }
    public PermitCompletionEvidence? HsseCompletion { get; private set; }
    public PermitCompletionEvidence? AreaOwnerCompletion { get; private set; }
    public IReadOnlyList<DomainEvent> Events => _events;

    public static Permit CreateDraft(PermitDraft draft, DateTimeOffset now)
    {
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

        var permit = new Permit(Guid.CreateVersion7(), draft, now)
        {
            RenewedFromPermitId = sourcePermitId
        };
        permit.Raise("permit_renewal_draft_created", new
        {
            SourcePermitId = sourcePermitId,
            permit.Version
        });
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
        Guid? activeWorkPeriodId,
        string? suspensionReason,
        PermitValidationEvidence? hsseValidation = null,
        PermitValidationEvidence? gasDistributionValidation = null,
        PermitApprovalEvidence? approval = null,
        PermitSuspensionEvidence? suspension = null,
        PermitCompletionEvidence? sponsorCompletion = null,
        PermitCompletionEvidence? hsseCompletion = null,
        PermitCompletionEvidence? areaOwnerCompletion = null,
        Guid? renewedFromPermitId = null,
        Guid? renewalPermitId = null) =>
        new(id, draft, createdAt)
        {
            PermitNumber = permitNumber,
            Status = status,
            Version = version,
            UpdatedAt = updatedAt,
            ActiveWorkPeriodId = activeWorkPeriodId,
            SuspensionReason = suspensionReason,
            HsseValidation = hsseValidation,
            GasDistributionValidation = gasDistributionValidation,
            Approval = approval,
            Suspension = suspension,
            SponsorCompletion = sponsorCompletion,
            HsseCompletion = hsseCompletion,
            AreaOwnerCompletion = areaOwnerCompletion,
            RenewedFromPermitId = renewedFromPermitId,
            RenewalPermitId = renewalPermitId
        };

    public void RequestRenewal(Permit renewal, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Open);
        EnsureActiveWorkPeriod();
        if (now < Draft.ValidFrom || now > Draft.ValidUntil)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.source_not_active",
                "Renewal hanya dapat diajukan ketika masa PTW asal sedang aktif.");
        }

        if (RenewalPermitId is not null)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.already_requested",
                "Renewal untuk PTW ini sudah pernah diajukan.");
        }

        if (renewal.RenewedFromPermitId != Id
            || !string.Equals(renewal.Draft.SponsorId, Draft.SponsorId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(renewal.Draft.LocationId, Draft.LocationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.renewal.source_mismatch",
                "Draft renewal harus terhubung ke Sponsor dan lokasi PTW asal.");
        }

        if (renewal.Draft.ValidFrom < Draft.ValidUntil)
        {
            throw new DomainRuleViolationException(
                "permit.renewal.validity_overlap",
                "Masa renewal harus dimulai pada atau setelah masa berlaku PTW asal berakhir.");
        }

        RenewalPermitId = renewal.Id;
        Version++;
        Touch(now);
        Raise("permit_renewal_requested", new
        {
            RenewalPermitId = renewal.Id,
            RenewalValidFrom = renewal.Draft.ValidFrom,
            RenewalValidUntil = renewal.Draft.ValidUntil,
            Version
        });
    }

    public void UpdateDraft(PermitDraft draft, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        Draft = NormalizeAndValidate(draft);
        ClearWorkflowEvidence();
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
        if (!readiness.IsReady)
        {
            throw new DomainRuleViolationException("permit.submit.requirements_incomplete", "PTW belum memenuhi seluruh persyaratan submit.");
        }

        if (string.IsNullOrWhiteSpace(permitNumber))
        {
            throw new DomainRuleViolationException("permit.number.required", "Nomor PTW resmi wajib tersedia saat submit.");
        }

        PermitNumber ??= permitNumber.Trim();
        MoveTo(PermitStatus.Submitted, "permit_submitted", now);
    }

    public void StartReview(DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Submitted);
        ClearWorkflowEvidence();
        MoveTo(PermitStatus.UnderReview, "review_started", now);
    }

    public void RequestRevision(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderReview, PermitStatus.AwaitingApproval);
        EnsureReason(reason);
        ClearWorkflowEvidence();
        MoveTo(PermitStatus.RevisionRequired, "revision_requested", now, new { Reason = reason.Trim() });
    }

    public void EndorseValidation(
        PermitValidationKind kind,
        string actorId,
        string statement,
        DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderReview);
        EnsureEvidence(actorId, statement);
        var evidence = new PermitValidationEvidence(
            kind,
            actorId.Trim(),
            statement.Trim(),
            now.ToUniversalTime());
        switch (kind)
        {
            case PermitValidationKind.Hsse when HsseValidation is null:
                HsseValidation = evidence;
                break;
            case PermitValidationKind.GasDistribution:
                throw new DomainRuleViolationException(
                    "permit.validation.retired",
                    "Validasi operasional tidak lagi menjadi bagian dari route PTW.");
            default:
                throw new DomainRuleViolationException(
                    "permit.validation.already_completed",
                    $"Validasi {kind} sudah diselesaikan untuk versi PTW ini.");
        }

        Touch(now);
        Raise("permit_validation_endorsed", new
        {
            Validation = kind.ToString(),
            evidence.ActorId,
            evidence.Statement
        });
        if (HsseValidation is not null)
        {
            MoveTo(PermitStatus.AwaitingApproval, "hsse_validation_completed", now);
        }
    }

    public void Approve(string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.AwaitingApproval);
        EnsureEvidence(actorId, statement);
        if (HsseValidation is null)
        {
            throw new DomainRuleViolationException(
                "permit.validation.incomplete",
                "Validasi HSSE wajib selesai sebelum approval.");
        }

        Approval = new PermitApprovalEvidence(actorId.Trim(), statement.Trim(), now.ToUniversalTime());
        MoveTo(PermitStatus.Approved, "permit_approved", now, new
        {
            Approval.ActorId,
            Approval.Statement
        });
    }

    public void MarkReadyForIssue(DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Approved);
        MoveTo(PermitStatus.ReadyForIssue, "readiness_completed", now);
    }

    public Guid OpenWorkPeriod(FieldIssueReadiness readiness, string actorId, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Approved, PermitStatus.ReadyForIssue);
        EnsureEvidence(actorId, "Penerbitan PTW");
        if (Approval is null
            || !string.Equals(Approval.ActorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.issue.area_owner_mismatch",
                "PTW hanya dapat diterbitkan oleh PIC pemilik area yang menyetujuinya.");
        }

        if (ActiveWorkPeriodId is not null)
        {
            throw new DomainRuleViolationException("work_period.already_active", "Hanya satu periode kerja yang boleh aktif.");
        }

        if (now < Draft.ValidFrom || now > Draft.ValidUntil)
        {
            throw new DomainRuleViolationException(
                "permit.outside_validity",
                "Waktu penerbitan berada di luar masa berlaku PTW.");
        }

        if (!readiness.IsReady)
        {
            throw new DomainRuleViolationException(
                "permit.issue.guards_failed",
                "Prasyarat aktual belum lengkap; PTW tidak dapat diterbitkan.");
        }

        ActiveWorkPeriodId = Guid.CreateVersion7();
        MoveTo(PermitStatus.Open, "permit_issued", now, new
        {
            WorkPeriodId = ActiveWorkPeriodId,
            IssuedBy = actorId.Trim()
        });
        return ActiveWorkPeriodId.Value;
    }

    public void RequestSuspension(string actorId, string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Open);
        EnsureEvidence(actorId, reason);
        if (!string.Equals(Draft.SponsorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.suspension.sponsor_mismatch",
                "Hanya Sponsor PTW yang dapat meminta penangguhan.");
        }

        EnsureActiveWorkPeriod();
        var periodId = ActiveWorkPeriodId;
        ActiveWorkPeriodId = null;
        SuspensionReason = reason.Trim();
        Suspension = new PermitSuspensionEvidence(
            actorId.Trim(),
            SuspensionReason,
            now.ToUniversalTime());
        MoveTo(PermitStatus.SuspensionRequested, "suspension_requested", now, new
        {
            WorkPeriodId = periodId,
            Suspension.RequestedBy,
            Suspension.Reason
        });
    }

    public void ApproveSuspension(string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.SuspensionRequested);
        EnsureEvidence(actorId, statement);
        if (Suspension is null)
        {
            throw new DomainRuleViolationException(
                "permit.suspension.evidence_missing",
                "Bukti permintaan penangguhan tidak tersedia.");
        }

        Suspension = Suspension with
        {
            ApprovedBy = actorId.Trim(),
            ApprovalStatement = statement.Trim(),
            ApprovedAt = now.ToUniversalTime()
        };
        MoveTo(PermitStatus.Suspended, "suspension_approved", now, new
        {
            Suspension.ApprovedBy,
            Suspension.ApprovalStatement
        });
    }

    public void ResolveSuspension(string resolution, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Suspended);
        EnsureReason(resolution);
        var previousReason = SuspensionReason;
        SuspensionReason = null;
        MoveTo(PermitStatus.ReadyForIssue, "permit_resumed", now, new { Reason = previousReason, Resolution = resolution.Trim() });
    }

    public void DeclareCompletion(string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Open);
        EnsureEvidence(actorId, statement);
        if (!string.Equals(Draft.SponsorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "permit.completion.sponsor_mismatch",
                "Hanya Sponsor PTW yang dapat menyatakan pekerjaan selesai.");
        }

        EnsureActiveWorkPeriod();
        var periodId = ActiveWorkPeriodId;
        ActiveWorkPeriodId = null;
        SponsorCompletion = new PermitCompletionEvidence(
            actorId.Trim(),
            statement.Trim(),
            now.ToUniversalTime());
        HsseCompletion = null;
        AreaOwnerCompletion = null;
        MoveTo(PermitStatus.CompletionConfirmationPending, "completion_declared", now, new
        {
            WorkPeriodId = periodId,
            SponsorCompletion.ActorId,
            SponsorCompletion.Statement
        });
    }

    public void ConfirmCompletion(
        PermitCompletionKind kind,
        string actorId,
        string statement,
        DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.CompletionConfirmationPending);
        EnsureEvidence(actorId, statement);
        var evidence = new PermitCompletionEvidence(
            actorId.Trim(),
            statement.Trim(),
            now.ToUniversalTime());
        switch (kind)
        {
            case PermitCompletionKind.Hsse when HsseCompletion is null:
                HsseCompletion = evidence;
                break;
            case PermitCompletionKind.AreaOwner when AreaOwnerCompletion is null:
                AreaOwnerCompletion = evidence;
                break;
            default:
                throw new DomainRuleViolationException(
                    "permit.completion.already_confirmed",
                    $"Konfirmasi penyelesaian {kind} sudah direkam.");
        }

        Touch(now);
        Raise("completion_confirmed", new
        {
            Confirmation = kind.ToString(),
            evidence.ActorId,
            evidence.Statement
        });
        if (HsseCompletion is not null && AreaOwnerCompletion is not null)
        {
            MoveTo(PermitStatus.WorkCompleted, "completion_confirmations_completed", now);
        }
    }

    public void Close(string actorId, string statement, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.WorkCompleted);
        EnsureEvidence(actorId, statement);
        if (SponsorCompletion is null || HsseCompletion is null || AreaOwnerCompletion is null)
        {
            throw new DomainRuleViolationException(
                "permit.completion.incomplete",
                "Konfirmasi Sponsor, HSSE, dan PIC pemilik area wajib lengkap sebelum PTW ditutup.");
        }

        MoveTo(PermitStatus.Closed, "permit_closed", now, new
        {
            ClosedBy = actorId.Trim(),
            Statement = statement.Trim()
        });
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderReview, PermitStatus.AwaitingApproval);
        EnsureReason(reason);
        MoveTo(PermitStatus.Rejected, "permit_rejected", now, new { Reason = reason.Trim() });
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.Submitted, PermitStatus.RevisionRequired);
        EnsureReason(reason);
        MoveTo(PermitStatus.Cancelled, "permit_cancelled", now, new { Reason = reason.Trim() });
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureStatus(
            PermitStatus.Approved,
            PermitStatus.ReadyForIssue,
            PermitStatus.SuspensionRequested,
            PermitStatus.Suspended);
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

    private static PermitDraft NormalizeAndValidate(PermitDraft value)
    {
        if (string.IsNullOrWhiteSpace(value.Title) || string.IsNullOrWhiteSpace(value.Description)
            || string.IsNullOrWhiteSpace(value.LocationId) || string.IsNullOrWhiteSpace(value.SponsorId)
            || string.IsNullOrWhiteSpace(value.PerformingAuthority) || string.IsNullOrWhiteSpace(value.Company))
        {
            throw new DomainRuleViolationException("permit.required_fields", "Data pekerjaan, lokasi, sponsor, pelaksana, dan perusahaan wajib diisi.");
        }

        var from = value.ValidFrom.ToUniversalTime();
        var until = value.ValidUntil.ToUniversalTime();
        if (from >= until)
        {
            throw new DomainRuleViolationException("permit.invalid_validity", "Waktu mulai harus lebih awal daripada waktu selesai.");
        }

        if (until - from > TimeSpan.FromDays(7))
        {
            throw new DomainRuleViolationException("permit.validity_exceeds_seven_days", "Masa berlaku PTW maksimum tujuh hari.");
        }

        if (value.Hazards.Count == 0 || value.Controls.Count == 0)
        {
            throw new DomainRuleViolationException("permit.hazard_control_required", "Sedikitnya satu bahaya dan satu kontrol wajib dicatat.");
        }

        return value with
        {
            Title = value.Title.Trim(),
            Description = value.Description.Trim(),
            LocationId = value.LocationId.Trim(),
            SponsorId = value.SponsorId.Trim(),
            PerformingAuthority = value.PerformingAuthority.Trim(),
            Company = value.Company.Trim(),
            ValidFrom = from,
            ValidUntil = until
        };
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
                "Identitas actor dan pernyataan keputusan wajib dicatat.");
        }
    }

    private void ClearWorkflowEvidence()
    {
        HsseValidation = null;
        GasDistributionValidation = null;
        Approval = null;
    }

    private void EnsureActiveWorkPeriod()
    {
        if (ActiveWorkPeriodId is null)
        {
            throw new DomainRuleViolationException("work_period.none_active", "Tidak ada periode kerja aktif.");
        }
    }
}
