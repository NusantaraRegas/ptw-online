namespace Ptw.Contracts;

public sealed record PolicyRequirementResponse(
    string Code,
    string Label,
    bool Satisfied,
    string Detail);

public sealed record OperationalPolicyReadinessResponse(
    bool EnforcementEnabled,
    bool ReadyForActivation,
    string Mode,
    string PolicyVersion,
    IReadOnlyList<PolicyRequirementResponse> Requirements,
    DateTimeOffset EvaluatedAt);

public sealed record PolicySimulationRequest(
    string SubjectId,
    string ActionCode,
    string LocationCode,
    IReadOnlyList<string> CompetencyCodes,
    DateTimeOffset? EvaluatedAt);

public sealed record PolicySimulationLocationResponse(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId);

public sealed record PolicySimulationAssignmentResponse(
    Guid Id,
    string RoleCode,
    string Kind,
    Guid? LocationId,
    bool IncludeDescendants,
    IReadOnlyList<string> RequiredCompetencyCodes,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);

public sealed record PolicySimulationCheckResponse(
    string Code,
    string Label,
    bool Passed,
    string Detail);

public sealed record PolicySimulationResponse(
    bool Allowed,
    string Outcome,
    string Code,
    string Summary,
    bool IsAuthoritative,
    bool EnforcementEnabled,
    string PolicyVersion,
    DateTimeOffset EvaluatedAt,
    PolicySimulationLocationResponse? Location,
    IReadOnlyList<PolicySimulationAssignmentResponse> Assignments,
    IReadOnlyList<string> RequiredCompetencyCodes,
    IReadOnlyList<string> MissingCompetencyCodes,
    IReadOnlyList<PolicySimulationCheckResponse> Checks);
