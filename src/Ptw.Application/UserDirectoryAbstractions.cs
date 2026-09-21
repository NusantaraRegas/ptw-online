using Ptw.Domain;

namespace Ptw.Application;

public sealed record StoredUserAccount(
    UserAccount Account,
    string ETag,
    UserSignatureEntry? Signature);

public sealed record UserSignatureEntry(
    Guid Id,
    string SubjectId,
    int Version,
    string MediaType,
    byte[] Content,
    string Sha256,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    bool IsActive);

public sealed record ResolvedUserIdentity(
    string SubjectId,
    string DisplayName,
    bool IsActive,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> LocationScopes,
    IReadOnlySet<string> CompetencyCodes);

public interface IUserDirectoryStore
{
    Task<IReadOnlyList<StoredUserAccount>> ListAsync(CancellationToken cancellationToken);
    Task<StoredUserAccount?> FindAsync(string subjectId, CancellationToken cancellationToken);
    Task<StoredUserAccount?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
    Task<ResolvedUserIdentity?> ResolveIdentityAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<UserSignatureEntry?> FindActiveSignatureAsync(string subjectId, CancellationToken cancellationToken);
    Task<StoredUserAccount> AddAsync(
        UserAccount account,
        string password,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken);
    Task<StoredUserAccount> UpdateAsync(
        UserAccount account,
        string expectedETag,
        Actor actor,
        string eventType,
        string correlationId,
        CancellationToken cancellationToken);
    Task<StoredUserAccount> ResetPasswordAsync(
        string subjectId,
        string password,
        string expectedETag,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken);
    Task<StoredUserAccount> AddSignatureAsync(
        string subjectId,
        string mediaType,
        byte[] content,
        string sha256,
        string expectedETag,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken);
}
