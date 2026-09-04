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

public sealed record PermitReasonRequest(string Reason);

public sealed record RequestPermitRenewalRequest(
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil);

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
    DateTimeOffset? ApprovedAt,
    PermitSuspensionResponse Suspension,
    PermitCompletionResponse Completion);

public sealed record PermitSuspensionResponse(
    bool Requested,
    string? RequestedBy,
    string? Reason,
    DateTimeOffset? RequestedAt,
    bool Approved,
    string? ApprovedBy,
    string? ApprovalStatement,
    DateTimeOffset? ApprovedAt);

public sealed record PermitCompletionResponse(
    PermitValidationResponse Sponsor,
    PermitValidationResponse Hsse,
    PermitValidationResponse AreaOwner);

public sealed record ConfirmPermitActionRequest(string Statement);

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
    Guid? RenewedFromPermitId,
    Guid? RenewalPermitId,
    PermitWorkflowResponse Workflow,
    string ETag);

public sealed record PermitRenewalResponse(
    int SourcePermitVersion,
    string SourceETag,
    PermitResponse Renewal);

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

public sealed record PermitTaskResponse(
    Guid Id,
    Guid PermitId,
    int PermitVersion,
    string Type,
    string Label,
    string RequiredRole,
    string Status,
    string? PermitNumber,
    string PermitTitle,
    string LocationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record PermitAttachmentResponse(
    Guid Id,
    Guid PermitId,
    int AddedInVersion,
    int? RemovedInVersion,
    string FileName,
    long SizeBytes,
    string MediaType,
    string Sha256,
    string ScanStatus,
    string UploadedBy,
    DateTimeOffset UploadedAt);

public sealed record PermitAttachmentMutationResponse(
    PermitAttachmentResponse Attachment,
    int PermitVersion,
    string ETag);

public sealed record MeResponse(
    string UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> LocationScopes,
    IReadOnlyList<string> CompetencyCodes,
    bool IsDevelopmentIdentity);
