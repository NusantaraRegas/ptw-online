using Ptw.Domain;

namespace Ptw.Application;

public sealed record StoredUserAuthorization(UserAuthorizationAssignment Entry, string ETag);

public sealed record AuthorizationCommandContext(
    string ActorId,
    string Operation,
    string Key,
    string RequestHash);

public sealed record AuthorizationResolution(
    bool IsResolved,
    string Code,
    IReadOnlyList<Guid> AssignmentIds,
    IReadOnlyList<string> RequiredCompetencyCodes);

public interface IUserAuthorizationStore
{
    Task<IReadOnlyList<StoredUserAuthorization>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredUserAuthorization>> ListApprovedForSubjectAsync(
        string subjectId,
        CancellationToken cancellationToken);
    Task<StoredUserAuthorization?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<StoredUserAuthorization> AddAsync(
        UserAuthorizationAssignment entry,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken);
    Task<StoredUserAuthorization> UpdateAsync(
        UserAuthorizationAssignment entry,
        string expectedETag,
        Actor actor,
        string correlationId,
        AuthorizationCommandContext? command,
        CancellationToken cancellationToken);
    Task<StoredUserAuthorization?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);
}

public interface IAuthorizationAssignmentResolver
{
    Task<AuthorizationResolution> ResolveAsync(
        string subjectId,
        string actionCode,
        Guid? locationId,
        DateTimeOffset instant,
        CancellationToken cancellationToken);
}
