namespace Ptw.Application;

public sealed record StoredUserGuide(
    Guid? Id,
    string FileName,
    long SizeBytes,
    string Sha256,
    string? StorageKey,
    string? ScanEvidenceReference,
    DateTimeOffset? ScannedAt,
    int Version,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string ETag);

public sealed record UserGuideCommandContext(
    string ActorId,
    string Operation,
    string Key,
    string RequestHash);

public interface IUserGuideStore
{
    Task<StoredUserGuide?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<StoredUserGuide?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);
    Task<StoredUserGuide> ReplaceAsync(
        StoredUserGuide replacement,
        string expectedETag,
        Actor actor,
        string correlationId,
        UserGuideCommandContext command,
        CancellationToken cancellationToken);
}

public sealed record UserGuideDownload(
    Stream Content,
    string FileName,
    string MediaType,
    long SizeBytes);
