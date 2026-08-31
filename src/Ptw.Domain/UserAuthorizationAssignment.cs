namespace Ptw.Domain;

public enum AuthorizationAssignmentKind
{
    Direct,
    Delegation
}

public enum AuthorizationAssignmentStatus
{
    Draft,
    PendingApproval,
    Approved
}

public sealed class UserAuthorizationAssignment
{
    private readonly List<MasterDataEvent> _events = [];

    private UserAuthorizationAssignment(
        Guid id,
        string subjectId,
        string roleCode,
        IReadOnlyList<string> actionCodes,
        Guid? locationId,
        bool includeDescendants,
        IReadOnlyList<string> requiredCompetencyCodes,
        AuthorizationAssignmentKind kind,
        Guid? sourceAuthorizationId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        string makerId,
        DateTimeOffset now)
    {
        Id = id;
        ApplyDraft(
            subjectId,
            roleCode,
            actionCodes,
            locationId,
            includeDescendants,
            requiredCompetencyCodes,
            kind,
            sourceAuthorizationId,
            effectiveFrom,
            effectiveUntil);
        Status = AuthorizationAssignmentStatus.Draft;
        Version = 1;
        MakerId = Required(makerId, "Maker");
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }
    public string SubjectId { get; private set; } = null!;
    public string RoleCode { get; private set; } = null!;
    public IReadOnlyList<string> ActionCodes { get; private set; } = [];
    public Guid? LocationId { get; private set; }
    public bool IncludeDescendants { get; private set; }
    public IReadOnlyList<string> RequiredCompetencyCodes { get; private set; } = [];
    public AuthorizationAssignmentKind Kind { get; private set; }
    public Guid? SourceAuthorizationId { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveUntil { get; private set; }
    public AuthorizationAssignmentStatus Status { get; private set; }
    public int Version { get; private set; }
    public string MakerId { get; private set; } = null!;
    public string? CheckerId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<MasterDataEvent> Events => _events;

    public static UserAuthorizationAssignment CreateDraft(
        string subjectId,
        string roleCode,
        IReadOnlyList<string> actionCodes,
        Guid? locationId,
        bool includeDescendants,
        IReadOnlyList<string> requiredCompetencyCodes,
        AuthorizationAssignmentKind kind,
        Guid? sourceAuthorizationId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        string makerId,
        DateTimeOffset now)
    {
        var entry = new UserAuthorizationAssignment(
            Guid.CreateVersion7(),
            subjectId,
            roleCode,
            actionCodes,
            locationId,
            includeDescendants,
            requiredCompetencyCodes,
            kind,
            sourceAuthorizationId,
            effectiveFrom,
            effectiveUntil,
            makerId,
            now);
        entry.Raise("authorization_draft_created", new { entry.SubjectId, entry.RoleCode, entry.Version });
        return entry;
    }

    public static UserAuthorizationAssignment Rehydrate(
        Guid id,
        string subjectId,
        string roleCode,
        IReadOnlyList<string> actionCodes,
        Guid? locationId,
        bool includeDescendants,
        IReadOnlyList<string> requiredCompetencyCodes,
        AuthorizationAssignmentKind kind,
        Guid? sourceAuthorizationId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        AuthorizationAssignmentStatus status,
        int version,
        string makerId,
        string? checkerId,
        DateTimeOffset? approvedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(
            id,
            subjectId,
            roleCode,
            actionCodes,
            locationId,
            includeDescendants,
            requiredCompetencyCodes,
            kind,
            sourceAuthorizationId,
            effectiveFrom,
            effectiveUntil,
            makerId,
            createdAt)
        {
            Status = status,
            Version = version,
            CheckerId = checkerId,
            ApprovedAt = approvedAt,
            UpdatedAt = updatedAt.ToUniversalTime()
        };

    public void UpdateDraft(
        string subjectId,
        string roleCode,
        IReadOnlyList<string> actionCodes,
        Guid? locationId,
        bool includeDescendants,
        IReadOnlyList<string> requiredCompetencyCodes,
        AuthorizationAssignmentKind kind,
        Guid? sourceAuthorizationId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        string actorId,
        DateTimeOffset now)
    {
        EnsureStatus(AuthorizationAssignmentStatus.Draft);
        ApplyDraft(
            subjectId,
            roleCode,
            actionCodes,
            locationId,
            includeDescendants,
            requiredCompetencyCodes,
            kind,
            sourceAuthorizationId,
            effectiveFrom,
            effectiveUntil);
        MakerId = Required(actorId, "Maker");
        CheckerId = null;
        ApprovedAt = null;
        Touch(now);
        Raise("authorization_draft_updated", new { SubjectId, RoleCode, Version });
    }

    public void SubmitForApproval(string actorId, DateTimeOffset now)
    {
        EnsureStatus(AuthorizationAssignmentStatus.Draft);
        MakerId = Required(actorId, "Maker");
        Status = AuthorizationAssignmentStatus.PendingApproval;
        Touch(now);
        Raise("authorization_submitted_for_approval", new { SubjectId, RoleCode, Version });
    }

    public void Approve(string checkerId, DateTimeOffset now)
    {
        EnsureStatus(AuthorizationAssignmentStatus.PendingApproval);
        var checker = Required(checkerId, "Checker");
        if (string.Equals(checker, MakerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "authorization.maker_checker_required",
                "Pembuat assignment tidak boleh menyetujui assignment yang sama.");
        }

        CheckerId = checker;
        ApprovedAt = now.ToUniversalTime();
        Status = AuthorizationAssignmentStatus.Approved;
        Touch(now);
        Raise("authorization_approved", new { SubjectId, RoleCode, Version, CheckerId });
    }

    public void ReturnForChanges(string checkerId, string reason, DateTimeOffset now)
    {
        EnsureStatus(AuthorizationAssignmentStatus.PendingApproval);
        var checker = Required(checkerId, "Checker");
        if (string.Equals(checker, MakerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "authorization.maker_checker_required",
                "Pembuat assignment tidak boleh memeriksa assignment yang sama.");
        }

        var normalizedReason = Required(reason, "Alasan pengembalian");
        CheckerId = checker;
        Status = AuthorizationAssignmentStatus.Draft;
        Touch(now);
        Raise("authorization_returned_for_changes", new { SubjectId, RoleCode, Version, Reason = normalizedReason });
    }

    public bool IsEffectiveAt(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return Status == AuthorizationAssignmentStatus.Approved
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
        string subjectId,
        string roleCode,
        IReadOnlyList<string> actionCodes,
        Guid? locationId,
        bool includeDescendants,
        IReadOnlyList<string> requiredCompetencyCodes,
        AuthorizationAssignmentKind kind,
        Guid? sourceAuthorizationId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil)
    {
        if (sourceAuthorizationId == Id)
        {
            throw new DomainRuleViolationException(
                "authorization.source_self",
                "Assignment tidak dapat menjadi sumber delegasinya sendiri.");
        }

        if (kind == AuthorizationAssignmentKind.Direct && sourceAuthorizationId is not null)
        {
            throw new DomainRuleViolationException(
                "authorization.direct_has_source",
                "Assignment langsung tidak boleh memiliki sumber delegasi.");
        }

        if (kind == AuthorizationAssignmentKind.Delegation && sourceAuthorizationId is null)
        {
            throw new DomainRuleViolationException(
                "authorization.delegation_source_required",
                "Delegasi wajib merujuk assignment langsung yang menjadi sumber authority.");
        }

        if (includeDescendants && locationId is null)
        {
            throw new DomainRuleViolationException(
                "authorization.descendants_require_location",
                "Include descendants hanya dapat digunakan bersama lokasi tertentu.");
        }

        var from = effectiveFrom.ToUniversalTime();
        var until = effectiveUntil?.ToUniversalTime();
        if (until is not null && until <= from)
        {
            throw new DomainRuleViolationException(
                "authorization.invalid_effective_period",
                "Tanggal akhir efektif harus lebih besar daripada tanggal mulai efektif.");
        }

        if (kind == AuthorizationAssignmentKind.Delegation && until is null)
        {
            throw new DomainRuleViolationException(
                "authorization.delegation_end_required",
                "Delegasi wajib memiliki tanggal akhir efektif.");
        }

        SubjectId = Required(subjectId, "User ID");
        RoleCode = Required(roleCode, "Role code", 100);
        ActionCodes = NormalizeCodes(actionCodes, "Action", false);
        LocationId = locationId;
        IncludeDescendants = includeDescendants;
        RequiredCompetencyCodes = NormalizeCodes(requiredCompetencyCodes, "Kompetensi", true);
        Kind = kind;
        SourceAuthorizationId = sourceAuthorizationId;
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

    private void EnsureStatus(params AuthorizationAssignmentStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainRuleViolationException(
                "authorization.invalid_transition",
                $"Aksi tidak diizinkan dari status {Status}.");
        }
    }

    private static string[] NormalizeCodes(
        IReadOnlyList<string> values,
        string label,
        bool allowEmpty)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Required(value, label, 100))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!allowEmpty && normalized.Length == 0)
        {
            throw new DomainRuleViolationException(
                "authorization.action_required",
                "Sedikitnya satu action code wajib dipilih.");
        }

        if (normalized.Length > 50)
        {
            throw new DomainRuleViolationException(
                "authorization.too_many_codes",
                $"{label} tidak boleh melebihi 50 nilai.");
        }

        return normalized;
    }

    private static string Required(string value, string label, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException("authorization.required_field", $"{label} wajib diisi.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainRuleViolationException(
                "authorization.field_too_long",
                $"{label} tidak boleh melebihi {maxLength} karakter.");
        }

        return normalized;
    }
}
