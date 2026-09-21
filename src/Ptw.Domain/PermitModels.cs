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
    string? ActorName = null);

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

public sealed record PermitClosureEvidence(
    Guid PrintPackageId,
    IReadOnlyList<Guid> AttachmentIds,
    string RequestedBy,
    string CompletionStatement,
    DateTimeOffset RequestedAt,
    int Revision,
    string? ReplacementReason = null);

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
    bool EvidenceReadable = false);

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
    string? ESimiExternalId,
    string? ESimiNumber,
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
    bool ESimiEligible,
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements)
{
    public bool IsReady => ESimiEligible && RulesEvaluated && RequiredDocumentsSafe && MissingRequirements.Count == 0;
}
