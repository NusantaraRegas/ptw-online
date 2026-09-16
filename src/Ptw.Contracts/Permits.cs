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
    string? AdditionalHazardReference = null);

public sealed record PermitWorkTypeOptionResponse(string Code, string Label, bool RequiresDetail);

public sealed record PermitWorkTypeCatalogResponse(
    string PermitClass,
    IReadOnlyList<PermitWorkTypeOptionResponse> Options);

public sealed record PermitSafetyEquipmentOptionResponse(string Code, string Label);

public sealed record PermitSafetyEquipmentCatalogResponse(
    string PermitClass,
    IReadOnlyList<PermitSafetyEquipmentOptionResponse> Options);

public sealed record PermitSupportingDocumentOptionResponse(
    string Code,
    string Label,
    int TemplateColumn,
    int TemplateIndex,
    bool Required,
    bool RequiresMetadata);

public sealed record SubmitPermitRequest(
    bool ESimiEligible,
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements);

public sealed record ValidateSubmissionRequest(
    string Statement,
    IReadOnlyList<string> SafetyEquipmentCodes);

public sealed record ApproveAndIssuePermitRequest(
    string Statement,
    Guid? ActingAssignmentId);

public sealed record PermitReasonRequest(string Reason);

public sealed record RequestPermitRenewalRequest(
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil);

public sealed record ResolveSuspensionRequest(string Resolution);

public sealed record RequestClosureRequest(
    Guid PrintPackageId,
    IReadOnlyList<Guid> SignedFieldCopyAttachmentIds,
    string CompletionStatement,
    bool AllPagesReviewed,
    bool ReadableAndCompleteAcknowledged);

public sealed record ClosePermitRequest(
    string Statement,
    bool CompletionConfirmed,
    bool HandbackConfirmed,
    bool EvidenceReadable);

public sealed record PermitValidationResponse(
    string Code,
    string Label,
    bool Completed,
    string? ActorId,
    string? Statement,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> SafetyEquipmentCodes);

public sealed record PermitApprovalResponse(
    bool Completed,
    string? ActorId,
    string? ActorPosition,
    string? Capacity,
    string? PrincipalManagerUserId,
    string? PrincipalPosition,
    Guid? AuthorizationId,
    Guid? ActingAssignmentId,
    string? Statement,
    DateTimeOffset? ApprovedAt);

public sealed record PermitSuspensionResponse(
    bool Suspended,
    string? SuspendedBy,
    string? Reason,
    DateTimeOffset? SuspendedAt,
    string? ResolvedBy,
    string? Resolution,
    DateTimeOffset? ResolvedAt);

public sealed record PermitClosureResponse(
    bool Requested,
    Guid? PrintPackageId,
    IReadOnlyList<Guid> SignedFieldCopyAttachmentIds,
    string? RequestedBy,
    string? CompletionStatement,
    DateTimeOffset? RequestedAt,
    int Revision,
    string? ReplacementReason,
    bool Closed,
    string? ClosedBy,
    string? CloseStatement,
    DateTimeOffset? ClosedAt);

public sealed record PermitWorkflowResponse(
    PermitValidationResponse Hse,
    PermitApprovalResponse Approval,
    PermitSuspensionResponse Suspension,
    PermitClosureResponse Closure);

public sealed record PermitResponse(
    Guid Id,
    string? PermitNumber,
    string Status,
    int Version,
    PermitDraftRequest Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
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
    string? ScanEvidenceReference,
    DateTimeOffset? ScannedAt,
    string Category,
    string? SupportingDocumentCode,
    string? DocumentNumber,
    string? DocumentRevision,
    DateTimeOffset? DocumentDate,
    int TargetPermitVersion,
    Guid? PrintPackageId,
    Guid? SupersedesAttachmentId,
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
