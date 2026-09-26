namespace Ptw.Infrastructure.Persistence;

public sealed class PermitRecord
{
    public Guid Id { get; set; }
    public string? PermitNumber { get; set; }
    public string Status { get; set; } = null!;
    /// <summary>Legacy aggregate mutation sequence stored in the original Version column.</summary>
    public int Version { get; set; }
    public int BusinessVersion { get; set; }
    public string LocationId { get; set; } = null!;
    public string SponsorId { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public string DraftJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? ActiveWorkPeriodId { get; set; }
    public Guid? RenewedFromPermitId { get; set; }
    public string? SuspensionReason { get; set; }
    public string? WorkflowEvidenceJson { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PermitVersionRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int Version { get; set; }
    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public sealed class PermitRevisionRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int Version { get; set; }
    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public sealed class AuditEventRecord
{
    public long Sequence { get; set; }
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public string EventType { get; set; } = null!;
    public string ActorId { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
}

public sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid PermitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class PermitTaskRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int PermitVersion { get; set; }
    public int? LegacyPermitVersion { get; set; }
    public int BusinessPermitVersion { get; set; }
    public string Type { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string RequiredRole { get; set; } = null!;
    public string? AssignedActorId { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public sealed class PermitDecisionRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int PermitVersion { get; set; }
    public int? LegacyPermitVersion { get; set; }
    public int BusinessPermitVersion { get; set; }
    public Guid TaskId { get; set; }
    public string Decision { get; set; } = null!;
    public string ActorId { get; set; } = null!;
    public string ActorPosition { get; set; } = null!;
    public string ApprovalCapacity { get; set; } = null!;
    public string PrincipalManagerUserId { get; set; } = null!;
    public string PrincipalPosition { get; set; } = null!;
    public Guid AuthorizationId { get; set; }
    public Guid? ActingAssignmentId { get; set; }
    public string Statement { get; set; } = null!;
    public DateTimeOffset DecidedAt { get; set; }
    public string EvidenceHash { get; set; } = null!;
}

public sealed class PrintPackageSnapshotRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int PermitVersion { get; set; }
    public int? LegacyPermitVersion { get; set; }
    public int BusinessPermitVersion { get; set; }
    public Guid DecisionId { get; set; }
    public string RuleVersion { get; set; } = null!;
    public string PrintTemplateVersion { get; set; } = null!;
    public string CampaignAssetVersion { get; set; } = null!;
    public string SnapshotJson { get; set; } = null!;
    public string SnapshotHash { get; set; } = null!;
    public string RenderStatus { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GeneratedDocumentRecord
{
    public Guid Id { get; set; }
    public Guid PrintPackageSnapshotId { get; set; }
    public string? StorageKey { get; set; }
    public string MediaType { get; set; } = "application/pdf";
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string RenderStatus { get; set; } = null!;
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Renderer build that produced the file, so a non-deterministic retry stays explainable.</summary>
    public string? RendererVersion { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PermitAttachmentRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int AddedInVersion { get; set; }
    public int? RemovedInVersion { get; set; }
    public int? LegacyAddedInVersion { get; set; }
    public int? LegacyRemovedInVersion { get; set; }
    public int AddedInBusinessVersion { get; set; }
    public int? RemovedInBusinessVersion { get; set; }
    public string FileName { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string MediaType { get; set; } = null!;
    public string Sha256 { get; set; } = null!;
    public string StorageKey { get; set; } = null!;
    public string ScanStatus { get; set; } = null!;
    public string? ScanEvidenceReference { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
    public string Category { get; set; } = null!;
    public string? SupportingDocumentCode { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentRevision { get; set; }
    public DateTimeOffset? DocumentDate { get; set; }
    public int TargetPermitVersion { get; set; }
    public int? LegacyTargetPermitVersion { get; set; }
    public int TargetBusinessPermitVersion { get; set; }
    public Guid? PrintPackageId { get; set; }
    public Guid? SupersedesAttachmentId { get; set; }
    public string UploadedBy { get; set; } = null!;
    public DateTimeOffset UploadedAt { get; set; }
    public string? RemovedBy { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PermitAttachmentCommandReceiptRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid PermitId { get; set; }
    public Guid AttachmentId { get; set; }
    public int ResultVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class LocationMasterRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public string Status { get; set; } = null!;
    public int Version { get; set; }
    public string MakerId { get; set; } = null!;
    public string? CheckerId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class LocationMasterVersionRecord
{
    public Guid Id { get; set; }
    public Guid LocationMasterId { get; set; }
    public int Version { get; set; }
    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public sealed class ConfigurationAuditEventRecord
{
    public long Sequence { get; set; }
    public Guid Id { get; set; }
    public string AggregateType { get; set; } = null!;
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = null!;
    public string ActorId { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
}

public sealed class DemoModeSettingRecord
{
    public Guid Id { get; set; }
    public bool Enabled { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DemoModeCommandReceiptRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public bool ResultEnabled { get; set; }
    public int ResultVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UserGuideSettingRecord
{
    public Guid Id { get; set; }
    public Guid CurrentVersionId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = null!;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class UserGuideVersionRecord
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string FileName { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = null!;
    public string StorageKey { get; set; } = null!;
    public string ScanEvidenceReference { get; set; } = null!;
    public DateTimeOffset ScannedAt { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string UploadedBy { get; set; } = null!;
}

public sealed class UserGuideCommandReceiptRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid ResultVersionId { get; set; }
    public int ResultSettingVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class LocationCommandReceiptRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid LocationMasterId { get; set; }
    public int ResultVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UserAuthorizationRecord
{
    public Guid Id { get; set; }
    public string SubjectId { get; set; } = null!;
    public string RoleCode { get; set; } = null!;
    public string ActionCodesJson { get; set; } = null!;
    public Guid? LocationId { get; set; }
    public bool IncludeDescendants { get; set; }
    public string RequiredCompetencyCodesJson { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public Guid? SourceAuthorizationId { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public string Status { get; set; } = null!;
    public int Version { get; set; }
    public string MakerId { get; set; } = null!;
    public string? CheckerId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class UserAuthorizationVersionRecord
{
    public Guid Id { get; set; }
    public Guid UserAuthorizationId { get; set; }
    public int Version { get; set; }
    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public sealed class AuthorizationCommandReceiptRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid UserAuthorizationId { get; set; }
    public int ResultVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UserAccountRecord
{
    public string SubjectId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string NormalizedUserName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Position { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string SecurityStamp { get; set; } = null!;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only login journal; rows are inserted once and never updated or deleted by the application.</summary>
public sealed class LoginAuditEventRecord
{
    public long Sequence { get; set; }
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string UserName { get; set; } = null!;
    public string? SubjectId { get; set; }
    public string DirectoryResult { get; set; } = null!;
    public string IdentitySource { get; set; } = null!;
    public string Outcome { get; set; } = null!;
    public string SourceAddress { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
}

public sealed class UserCredentialRecord
{
    public string SubjectId { get; set; } = null!;
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordHash { get; set; } = [];
    public int Iterations { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset PasswordChangedAt { get; set; }
}

public sealed class UserSignatureVersionRecord
{
    public Guid Id { get; set; }
    public string SubjectId { get; set; } = null!;
    public int Version { get; set; }
    public string MediaType { get; set; } = null!;
    public byte[] Content { get; set; } = [];
    public string Sha256 { get; set; } = null!;
    public string UploadedBy { get; set; } = null!;
    public DateTimeOffset UploadedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
}

public sealed class PolicyUatSuiteRecord
{
    public Guid Id { get; set; }
    public string SuiteKey { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string PolicyVersion { get; set; } = null!;
    public int Version { get; set; }
    public string ScenariosJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public sealed class PolicyUatRunRecord
{
    public Guid Id { get; set; }
    public Guid PolicyUatSuiteId { get; set; }
    public string PolicyVersion { get; set; } = null!;
    public string SuiteContentHash { get; set; } = null!;
    public bool Passed { get; set; }
    public int ScenarioCount { get; set; }
    public int MatchedCount { get; set; }
    public string CoverageJson { get; set; } = null!;
    public string ResultsJson { get; set; } = null!;
    public string ReportHash { get; set; } = null!;
    public DateTimeOffset ExecutedAt { get; set; }
    public string ExecutedBy { get; set; } = null!;
}

public sealed class PolicyUatCommandReceiptRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid? PolicyUatSuiteId { get; set; }
    public Guid? PolicyUatRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
