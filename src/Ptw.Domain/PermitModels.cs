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
    DateTimeOffset ValidatedAt);

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
    DateTimeOffset ApprovedAt);

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
    DateTimeOffset ClosedAt);

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
    DateTimeOffset? JsaDate = null);

public sealed record SubmissionReadiness(
    bool ESimiEligible,
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements)
{
    public bool IsReady => ESimiEligible && RulesEvaluated && RequiredDocumentsSafe && MissingRequirements.Count == 0;
}
