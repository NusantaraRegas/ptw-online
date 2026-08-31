namespace Ptw.Domain;

public enum LocationMasterStatus
{
    Draft,
    PendingApproval,
    Approved
}

public sealed record MasterDataEvent(
    Guid Id,
    Guid AggregateId,
    string Type,
    DateTimeOffset OccurredAt,
    object Payload);

public sealed class LocationMasterEntry
{
    private readonly List<MasterDataEvent> _events = [];

    private LocationMasterEntry(
        Guid id,
        string code,
        string name,
        Guid? parentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        string makerId,
        DateTimeOffset now)
    {
        Id = id;
        ApplyDraft(code, name, parentId, effectiveFrom, effectiveUntil);
        Status = LocationMasterStatus.Draft;
        Version = 1;
        MakerId = Required(makerId, "Maker");
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveUntil { get; private set; }
    public LocationMasterStatus Status { get; private set; }
    public int Version { get; private set; }
    public string MakerId { get; private set; } = null!;
    public string? CheckerId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<MasterDataEvent> Events => _events;

    public static LocationMasterEntry CreateDraft(
        string code,
        string name,
        Guid? parentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        string makerId,
        DateTimeOffset now)
    {
        var entry = new LocationMasterEntry(
            Guid.CreateVersion7(),
            code,
            name,
            parentId,
            effectiveFrom,
            effectiveUntil,
            makerId,
            now);
        entry.Raise("location_draft_created", new { entry.Code, entry.Version });
        return entry;
    }

    public static LocationMasterEntry Rehydrate(
        Guid id,
        string code,
        string name,
        Guid? parentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        LocationMasterStatus status,
        int version,
        string makerId,
        string? checkerId,
        DateTimeOffset? approvedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, code, name, parentId, effectiveFrom, effectiveUntil, makerId, createdAt)
        {
            Status = status,
            Version = version,
            CheckerId = checkerId,
            ApprovedAt = approvedAt,
            UpdatedAt = updatedAt.ToUniversalTime()
        };

    public void UpdateDraft(
        string code,
        string name,
        Guid? parentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        string actorId,
        DateTimeOffset now)
    {
        EnsureStatus(LocationMasterStatus.Draft);
        ApplyDraft(code, name, parentId, effectiveFrom, effectiveUntil);
        MakerId = Required(actorId, "Maker");
        CheckerId = null;
        ApprovedAt = null;
        Touch(now);
        Raise("location_draft_updated", new { Code, Version });
    }

    public void SubmitForApproval(string actorId, DateTimeOffset now)
    {
        EnsureStatus(LocationMasterStatus.Draft);
        MakerId = Required(actorId, "Maker");
        Status = LocationMasterStatus.PendingApproval;
        Touch(now);
        Raise("location_submitted_for_approval", new { Code, Version });
    }

    public void Approve(string checkerId, DateTimeOffset now)
    {
        EnsureStatus(LocationMasterStatus.PendingApproval);
        var checker = Required(checkerId, "Checker");
        if (string.Equals(checker, MakerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "location.maker_checker_required",
                "Pembuat perubahan tidak boleh menyetujui perubahan master yang sama.");
        }

        CheckerId = checker;
        ApprovedAt = now.ToUniversalTime();
        Status = LocationMasterStatus.Approved;
        Touch(now);
        Raise("location_approved", new { Code, Version, CheckerId });
    }

    public void ReturnForChanges(string checkerId, string reason, DateTimeOffset now)
    {
        EnsureStatus(LocationMasterStatus.PendingApproval);
        var checker = Required(checkerId, "Checker");
        if (string.Equals(checker, MakerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "location.maker_checker_required",
                "Pembuat perubahan tidak boleh memeriksa perubahan master yang sama.");
        }

        var normalizedReason = Required(reason, "Alasan pengembalian");
        CheckerId = checker;
        Status = LocationMasterStatus.Draft;
        Touch(now);
        Raise("location_returned_for_changes", new { Code, Version, Reason = normalizedReason });
    }

    public bool IsEffectiveAt(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return Status == LocationMasterStatus.Approved
            && EffectiveFrom <= utc
            && (EffectiveUntil is null || utc < EffectiveUntil);
    }

    public IReadOnlyList<MasterDataEvent> DequeueEvents()
    {
        var result = _events.ToArray();
        _events.Clear();
        return result;
    }

    private void ApplyDraft(
        string code,
        string name,
        Guid? parentId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil)
    {
        if (parentId == Id)
        {
            throw new DomainRuleViolationException("location.parent_self", "Lokasi tidak dapat menjadi induknya sendiri.");
        }

        var from = effectiveFrom.ToUniversalTime();
        var until = effectiveUntil?.ToUniversalTime();
        if (until is not null && until <= from)
        {
            throw new DomainRuleViolationException(
                "location.invalid_effective_period",
                "Tanggal akhir efektif harus lebih besar daripada tanggal mulai efektif.");
        }

        Code = Required(code, "Kode lokasi", 100);
        Name = Required(name, "Nama lokasi", 200);
        ParentId = parentId;
        EffectiveFrom = from;
        EffectiveUntil = until;
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now.ToUniversalTime();
    }

    private void Raise(string type, object payload) =>
        _events.Add(new MasterDataEvent(Guid.CreateVersion7(), Id, type, UpdatedAt, payload));

    private void EnsureStatus(params LocationMasterStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainRuleViolationException(
                "location.invalid_transition",
                $"Aksi tidak diizinkan dari status {Status}.");
        }
    }

    private static string Required(string value, string label, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException("location.required_field", $"{label} wajib diisi.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException(
                "location.field_too_long",
                $"{label} tidak boleh melebihi {maxLength} karakter.");
        }

        return normalized;
    }
}
