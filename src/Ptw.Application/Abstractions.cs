using Ptw.Domain;

namespace Ptw.Application;

public sealed record StoredPermit(Permit Permit, string ETag);
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
