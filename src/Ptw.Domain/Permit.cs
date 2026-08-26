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
    public string? SuspensionReason { get; private set; }
    public IReadOnlyList<DomainEvent> Events => _events;

    public static Permit CreateDraft(PermitDraft draft, DateTimeOffset now)
    {
        var permit = new Permit(Guid.CreateVersion7(), draft, now);
        permit.Raise("permit_draft_created", new { permit.Version });
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
        string? suspensionReason) =>
        new(id, draft, createdAt)
        {
            PermitNumber = permitNumber,
            Status = status,
            Version = version,
            UpdatedAt = updatedAt,
            ActiveWorkPeriodId = activeWorkPeriodId,
            SuspensionReason = suspensionReason
        };

    public void UpdateDraft(PermitDraft draft, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Draft, PermitStatus.RevisionRequired);
        Draft = NormalizeAndValidate(draft);
        Version++;
        Touch(now);
        Raise("permit_draft_updated", new { Version });
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
        MoveTo(PermitStatus.UnderReview, "review_started", now);
    }

    public void RequestRevision(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderReview, PermitStatus.AwaitingApproval);
        EnsureReason(reason);
        MoveTo(PermitStatus.RevisionRequired, "revision_requested", now, new { Reason = reason.Trim() });
    }

    public void EndorseAllReviews(DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.UnderReview);
        MoveTo(PermitStatus.AwaitingApproval, "reviews_endorsed", now);
    }

    public void Approve(DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.AwaitingApproval);
        MoveTo(PermitStatus.Approved, "permit_approved", now);
    }

    public void MarkReadyForIssue(DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Approved);
        MoveTo(PermitStatus.ReadyForIssue, "readiness_completed", now);
    }

    public Guid OpenWorkPeriod(FieldIssueReadiness readiness, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Approved, PermitStatus.ReadyForIssue);
        if (ActiveWorkPeriodId is not null)
        {
            throw new DomainRuleViolationException("work_period.already_active", "Hanya satu periode kerja yang boleh aktif.");
        }

        if (now < Draft.ValidFrom || now > Draft.ValidUntil)
        {
            throw new DomainRuleViolationException("permit.outside_validity", "Waktu pembukaan berada di luar masa berlaku PTW.");
        }

        if (!readiness.IsReady)
        {
            throw new DomainRuleViolationException("permit.open.guards_failed", "Prasyarat aktual belum lengkap; PTW tidak dapat dibuka.");
        }

        ActiveWorkPeriodId = Guid.CreateVersion7();
        MoveTo(PermitStatus.Open, "work_period_opened", now, new { WorkPeriodId = ActiveWorkPeriodId });
        return ActiveWorkPeriodId.Value;
    }

    public void CloseWorkPeriod(bool jobContinues, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Open);
        EnsureActiveWorkPeriod();
        var periodId = ActiveWorkPeriodId;
        ActiveWorkPeriodId = null;
        if (jobContinues)
        {
            MoveTo(PermitStatus.Approved, "work_period_closed", now, new { WorkPeriodId = periodId });
        }
        else
        {
            MoveTo(PermitStatus.WorkCompleted, "work_completed", now, new { WorkPeriodId = periodId });
        }
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Open, PermitStatus.Approved, PermitStatus.ReadyForIssue);
        EnsureReason(reason);
        var periodId = ActiveWorkPeriodId;
        ActiveWorkPeriodId = null;
        SuspensionReason = reason.Trim();
        MoveTo(PermitStatus.Suspended, "permit_suspended", now, new { Reason = SuspensionReason, WorkPeriodId = periodId });
    }

    public void ResolveSuspension(string resolution, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.Suspended);
        EnsureReason(resolution);
        var previousReason = SuspensionReason;
        SuspensionReason = null;
        MoveTo(PermitStatus.ReadyForIssue, "permit_resumed", now, new { Reason = previousReason, Resolution = resolution.Trim() });
    }

    public void AcceptHandback(HandbackReadiness readiness, DateTimeOffset now)
    {
        EnsureStatus(PermitStatus.WorkCompleted);
        if (!readiness.IsReady)
        {
            throw new DomainRuleViolationException("handback.incomplete", "Inspeksi, restorasi, dan penerimaan handback wajib lengkap.");
        }

        MoveTo(PermitStatus.Closed, "permit_closed", now);
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
        EnsureStatus(PermitStatus.Approved, PermitStatus.ReadyForIssue, PermitStatus.Suspended);
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

    private void EnsureActiveWorkPeriod()
    {
        if (ActiveWorkPeriodId is null)
        {
            throw new DomainRuleViolationException("work_period.none_active", "Tidak ada periode kerja aktif.");
        }
    }
}
