using Ptw.Domain;

namespace Ptw.Application;

public sealed record StoredPermit(Permit Permit, string ETag);
public sealed record StoredPermitRenewal(StoredPermit Source, StoredPermit Renewal);
public sealed record StorePage<T>(IReadOnlyList<T> Items, int Count);
public sealed record PermitActivityEntry(
    long Sequence,
    string EventType,
    string ActorId,
    DateTimeOffset OccurredAt,
    string PayloadJson,
    string CorrelationId);
public sealed record PermitVersionEntry(
    int Version,
    PermitDraft Draft,
    string ContentHash,
    DateTimeOffset CreatedAt,
    string CreatedBy);
public sealed record PermitTaskEntry(
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
public sealed record PermitAttachmentEntry(
    Guid Id,
    Guid PermitId,
    int AddedInVersion,
    int? RemovedInVersion,
    string FileName,
    long SizeBytes,
    string MediaType,
    string Sha256,
    string StorageKey,
    string ScanStatus,
    string UploadedBy,
    DateTimeOffset UploadedAt);
public sealed record StoredAttachmentContent(
    string StorageKey,
    long SizeBytes,
    string Sha256,
    string DetectedMediaType);
public sealed record StoredPermitAttachment(
    StoredPermit Permit,
    PermitAttachmentEntry Attachment);
public sealed record AttachmentPolicy(
    bool Enabled,
    long MaxFileBytes,
    int MaxFilesPerPermit,
    bool RequireMalwareScan);

public interface IPermitStore
{
    Task<StoredPermit?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredPermit>> ListAsync(string? sponsorId, CancellationToken cancellationToken);
    Task<StoredPermit> AddAsync(
        Permit permit,
        Actor actor,
        string correlationId,
        PolicyAuthorizationEvidence? authorizationEvidence,
        CancellationToken cancellationToken);
    Task<StoredPermitRenewal> AddRenewalAsync(
        Permit source,
        Permit renewal,
        string expectedSourceETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        PolicyAuthorizationEvidence? authorizationEvidence,
        CancellationToken cancellationToken);
    Task<StoredPermit> UpdateAsync(
        Permit permit,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext? idempotency,
        PolicyAuthorizationEvidence? authorizationEvidence,
        CancellationToken cancellationToken);
    Task<StoredPermit?> FindIdempotentResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);
    Task<StorePage<PermitActivityEntry>> ListActivityAsync(
        Guid permitId,
        int offset,
        int limit,
        CancellationToken cancellationToken);
    Task<StorePage<PermitVersionEntry>> ListVersionsAsync(
        Guid permitId,
        int offset,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PermitTaskEntry>> ListPendingTasksAsync(
        string actorId,
        IReadOnlySet<string> roles,
        IReadOnlySet<string> locationScopes,
        CancellationToken cancellationToken);
}

public interface IPermitAttachmentStore
{
    Task<IReadOnlyList<PermitAttachmentEntry>> ListActiveAsync(
        Guid permitId,
        CancellationToken cancellationToken);
    Task<PermitAttachmentEntry?> FindActiveAsync(
        Guid permitId,
        Guid attachmentId,
        CancellationToken cancellationToken);
    Task<StoredPermitAttachment?> FindIdempotentResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);
    Task<StoredPermitAttachment> AddAsync(
        Permit permit,
        PermitAttachmentEntry attachment,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        CancellationToken cancellationToken);
    Task<StoredPermitAttachment> RemoveAsync(
        Permit permit,
        PermitAttachmentEntry attachment,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        CancellationToken cancellationToken);
}

public interface IAttachmentStorage
{
    Task<StoredAttachmentContent> StoreAsync(
        Guid attachmentId,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteOrphanAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed record Actor(
    string Id,
    string DisplayName,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> LocationScopes,
    IReadOnlySet<string> CompetencyCodes);

public interface IActorContext
{
    Actor Current { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IPermitNumberGenerator
{
    string Generate(DateTimeOffset now);
}

public sealed record IdempotencyContext(string ActorId, string Operation, string Key, string RequestHash);

public sealed class ResourceNotFoundException(string resource, object id) : Exception($"{resource} '{id}' tidak ditemukan.");

public sealed class ConcurrencyConflictException() : Exception("Data PTW telah berubah. Muat ulang sebelum mengulangi aksi.");

public sealed class InvalidRequestException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PolicyActivationException(string message) : Exception(message);

public sealed class PolicyAuthorizationDeniedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
