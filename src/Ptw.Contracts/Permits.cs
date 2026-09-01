namespace Ptw.Contracts;

using System.Text.Json;

public sealed record PermitDraftRequest(
    string Title,
    string Description,
    string LocationId,
    string SponsorId,
    string PerformingAuthority,
    string Company,
    string PermitClass,
    string RiskLevel,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    string? ESimiExternalId,
    string? ESimiNumber,
    IReadOnlyList<string> Hazards,
    IReadOnlyList<string> Controls,
    IReadOnlyList<string> RequiredDocumentCodes);

public sealed record SubmitPermitRequest(
    bool ESimiEligible,
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements);

public sealed record EndorsePermitValidationRequest(string Statement);

public sealed record ApprovePermitRequest(string Statement);

public sealed record IssuePermitRequest(
    bool ESimiEligible,
    bool LocationVerified,
    bool ToolboxTalkComplete,
    bool PersonnelAcknowledged,
    bool PpeAndControlsVerified,
    bool IsolationVerified,
    bool SimopsVerified,
    bool GasTestSatisfied,
    bool HasUnresolvedSuspension);

public sealed record PermitValidationResponse(
    string Code,
    string Label,
    bool Completed,
    string? ActorId,
    string? Statement,
    DateTimeOffset? CompletedAt);

public sealed record PermitWorkflowResponse(
    PermitValidationResponse Hsse,
    PermitValidationResponse GasDistribution,
    string? ApprovedBy,
    string? ApprovalStatement,
    DateTimeOffset? ApprovedAt);

public sealed record PermitResponse(
    Guid Id,
    string? PermitNumber,
    string Status,
    int Version,
    PermitDraftRequest Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ActiveWorkPeriodId,
    string? SuspensionReason,
    PermitWorkflowResponse Workflow,
    string ETag);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Count);

public sealed record PermitActivityResponse(
    long Sequence,
    string EventType,
    string ActorId,
    DateTimeOffset OccurredAt,
    JsonElement Payload,
    string CorrelationId);

public sealed record PermitVersionResponse(
    int Version,
    PermitDraftRequest Snapshot,
    string ContentHash,
    DateTimeOffset CreatedAt,
    string CreatedBy);

public sealed record MeResponse(
    string UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> LocationScopes,
    IReadOnlyList<string> CompetencyCodes,
    bool IsDevelopmentIdentity);
