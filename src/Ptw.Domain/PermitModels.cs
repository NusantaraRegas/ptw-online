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

public sealed record HandbackReadiness(
    bool AreaInspected,
    bool HousekeepingComplete,
    bool PersonnelAndEquipmentClear,
    bool IsolationRestored,
    bool OperationsAccepted)
{
    public bool IsReady => AreaInspected && HousekeepingComplete && PersonnelAndEquipmentClear
        && IsolationRestored && OperationsAccepted;
}
