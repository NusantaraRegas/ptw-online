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

public enum PermitValidationKind
{
    Hsse,
    GasDistribution
}

public sealed record PermitValidationEvidence(
    PermitValidationKind Kind,
    string ActorId,
    string Statement,
    DateTimeOffset ValidatedAt);

public sealed record PermitApprovalEvidence(
    string ActorId,
    string Statement,
    DateTimeOffset ApprovedAt);

public sealed record PermitSuspensionEvidence(
    string RequestedBy,
    string Reason,
    DateTimeOffset RequestedAt,
    string? ApprovedBy = null,
    string? ApprovalStatement = null,
    DateTimeOffset? ApprovedAt = null);

public sealed record PermitCompletionEvidence(
    string ActorId,
    string Statement,
    DateTimeOffset ConfirmedAt);

public enum PermitCompletionKind
{
    Hsse,
    AreaOwner
}

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
    IReadOnlyList<string> RequiredDocumentCodes);

public sealed record SubmissionReadiness(
    bool ESimiEligible,
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements)
{
    public bool IsReady => ESimiEligible && RulesEvaluated && RequiredDocumentsSafe && MissingRequirements.Count == 0;
}

public sealed record FieldIssueReadiness(
    bool ESimiEligible,
    bool LocationVerified,
    bool ToolboxTalkComplete,
    bool PersonnelAcknowledged,
    bool PpeAndControlsVerified,
    bool IsolationVerified,
    bool SimopsVerified,
    bool GasTestSatisfied,
    bool HasUnresolvedSuspension)
{
    public bool IsReady => ESimiEligible && LocationVerified && ToolboxTalkComplete
        && PersonnelAcknowledged && PpeAndControlsVerified && IsolationVerified
        && SimopsVerified && GasTestSatisfied && !HasUnresolvedSuspension;
}
