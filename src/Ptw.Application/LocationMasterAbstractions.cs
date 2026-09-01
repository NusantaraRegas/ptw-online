using Ptw.Domain;

namespace Ptw.Application;

public sealed record StoredLocationMaster(LocationMasterEntry Entry, string ETag);

public sealed record LocationCommandContext(
    string ActorId,
    string Operation,
    string Key,
    string RequestHash);

public interface ILocationMasterStore
{
    Task<IReadOnlyList<StoredLocationMaster>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredLocationMaster>> FindApprovedEffectiveByCodeAsync(
        string code,
        DateTimeOffset instant,
        CancellationToken cancellationToken);
    Task<int> CountApprovedEffectiveAsync(DateTimeOffset instant, CancellationToken cancellationToken);
    Task<StoredLocationMaster?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<StoredLocationMaster> AddAsync(
        LocationMasterEntry entry,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken);
    Task<StoredLocationMaster> UpdateAsync(
        LocationMasterEntry entry,
        string expectedETag,
        Actor actor,
        string correlationId,
        LocationCommandContext? command,
        CancellationToken cancellationToken);
    Task<StoredLocationMaster?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);
}
