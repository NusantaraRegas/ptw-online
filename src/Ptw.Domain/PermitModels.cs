namespace Ptw.Domain;

public enum PermitClass
{
    HotWork,
    ColdWork,
    ConfinedSpaceEntry
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Extreme
}

public sealed record PermitValidationEvidence(
    string ActorId,
    string Statement,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<string>? SafetyEquipmentCodes = null,
    string? ActorName = null,
    bool RevisionSelfCheckConfirmed = false);

/// <summary>
/// Bagian 5 selection the PIC HSE saved before deciding the validation. It survives revision requests,
/// draft edits, and resubmission so the HSE never re-enters the same choice (UAT-2026-10-12 no. 8-9).
/// </summary>
public sealed record HseSafetyEquipmentSelection(
    string ActorId,
    IReadOnlyList<string> SafetyEquipmentCodes,
    DateTimeOffset SavedAt,
    string? ActorName = null);

public enum PermitInitiationStatus
{
    Drafting,
    AwaitingSponsorApproval,
    Returned,
    Approved
}

/// <summary>
/// Contractor-initiated draft that must be approved by the Sponsor before it can be submitted to the
/// PIC HSE (UAT-2026-10-12 no. 14). The lifecycle status stays DRAFT throughout.
/// </summary>
public sealed record PermitInitiation(
    string ContractorId,
    PermitInitiationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedToSponsorAt = null,
    string? SponsorStatement = null,
    DateTimeOffset? DecidedAt = null,
    string? ContractorName = null);

public sealed record AreaOperationsReviewEvidence(
    string ActorId,
    string ActorName,
    string ActorPosition,
    Guid AuthorizationId,
    IReadOnlyList<string> ConditionCodes,
    string? OtherConditionDetail,
    string Statement,
    DateTimeOffset ReviewedAt,
    VisualSignatureEvidence? Signature = null);

public sealed record VisualSignatureEvidence(
    Guid SignatureVersionId,
    int Version,
    string MediaType,
    byte[] Content,
    string Sha256);

public enum ApprovalCapacity
{
    Manager,
    ActingForManager
}

public sealed record PermitApprovalEvidence(
    string ActorId,
    string ActorPosition,
    ApprovalCapacity Capacity,
    string PrincipalManagerUserId,
    string PrincipalPosition,
    Guid AuthorizationId,
    Guid? ActingAssignmentId,
    string RuleVersion,
    string PrintTemplateVersion,
    string CampaignAssetVersion,
    string Statement,
    DateTimeOffset ApprovedAt,
    string? ActorName = null,
    VisualSignatureEvidence? Signature = null);

public sealed record PermitSuspensionEvidence(
    string SuspendedBy,
    string Reason,
    DateTimeOffset SuspendedAt,
    string? ResolvedBy = null,
    string? Resolution = null,
    DateTimeOffset? ResolvedAt = null);

/// <summary>
/// Bagian 9 of the controlled form, declared digitally by the Sponsor when requesting closure
/// (UAT-2026-10-12 no. 17). Null on closures recorded before the section moved off the hardcopy.
/// </summary>
public sealed record PermitWorkCompletionDeclaration(
    bool WorkCompleted,
    bool EquipmentAndMaterialsRemoved,
    string? Explanation,
    string PerformingAuthorityName,
    string PerformingAuthorityBadgeNumber,
    string SponsorBadgeNumber);

public sealed record PermitClosureEvidence(
    Guid PrintPackageId,
    IReadOnlyList<Guid> AttachmentIds,
    string RequestedBy,
    string CompletionStatement,
    DateTimeOffset RequestedAt,
    int Revision,
    string? ReplacementReason = null,
    PermitWorkCompletionDeclaration? Completion = null);

public sealed record PermitClosureDecisionEvidence(
    string ActorId,
    string Statement,
    DateTimeOffset ClosedAt,
    string OfficerName = "",
    bool WorkAreaInspectedAndClean = false,
    bool WorkCompleted = false,
    bool ManagerAgreesWorkCompleted = false,
    bool InhibitedSystemsRestored = false,
    bool AreaHandedBackAndSafeguardsRestored = false,
    bool EvidenceReadable = false,
    string? IncompleteWorkStatus = null);

public enum PermitRenewalReviewStatus
{
    Pending,
    RevisionRequired,
    Approved,
    Rejected
}

public sealed record PermitRenewalRequestEvidence(
    Guid PrintPackageId,
    IReadOnlyList<Guid> AttachmentIds,
    string RequestedBy,
    string ContinuationStatement,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset RequestedAt,
    int Revision,
    PermitRenewalReviewStatus Status,
    string? ReplacementReason = null,
    string? DecidedBy = null,
    string? DecisionStatement = null,
    DateTimeOffset? DecidedAt = null);

public sealed record PermitDraft(
    string Title,
    string Description,
    string LocationId,
    string SponsorId,
    string PerformingAuthority,
    string Company,
    PermitClass PermitClass,
    RiskLevel RiskLevel,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    IReadOnlyList<string> Hazards,
    IReadOnlyList<string> Controls,
    IReadOnlyList<string> RequiredDocumentCodes,
    string SubmitterType = "USER_SPONSOR",
    string? WorkTypeCode = null,
    string? EquipmentTag = null,
    string? PlantArea = null,
    bool ClsrApplicable = false,
    string? SimopsDeclaration = null,
    IReadOnlyList<string>? SafetyEquipmentCodes = null,
    IReadOnlyList<string>? IsolationPrecautionCodes = null,
    string? JsaDocumentNumber = null,
    string? JsaRevision = null,
    DateTimeOffset? JsaDate = null,
    IReadOnlyList<string>? WorkTypeCodes = null,
    string? OtherWorkTypeDescription = null,
    string? EquipmentName = null,
    string? WorkOrderNumber = null,
    string? AdditionalHazardReference = null,
    IReadOnlyList<string>? HeaderClassificationCodes = null);

public sealed record SubmissionReadiness(
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements,
    bool RevisionSelfCheckConfirmed = false)
{
    public bool IsReady => RulesEvaluated && RequiredDocumentsSafe && MissingRequirements.Count == 0;
}

/// <summary>
/// Official PTW number: <c>PTW/NNNN/DD/MM/YYYY</c> where NNNN is the yearly sequence and the date is the
/// WIB submission date (UAT-2026-10-12 no. 15). WIB has no daylight saving, so a fixed offset is exact.
/// </summary>
public static class PermitNumberFormat
{
    public static readonly TimeSpan WibOffset = TimeSpan.FromHours(7);

    public static int SequenceYear(DateTimeOffset instant) => instant.ToOffset(WibOffset).Year;

    public static string Compose(DateTimeOffset instant, int sequence)
    {
        if (sequence <= 0)
        {
            throw new DomainRuleViolationException("permit.number.sequence_invalid", "Nomor urut PTW harus positif.");
        }

        var wib = instant.ToOffset(WibOffset);
        return $"PTW/{sequence:0000}/{wib:dd}/{wib:MM}/{wib:yyyy}";
    }
}
