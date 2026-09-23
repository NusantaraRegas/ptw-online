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

/// <summary>
/// Outcome of verifying a credential against an external directory (Active Directory via LDAP).
/// </summary>
public enum DirectoryAuthenticationResult
{
    /// <summary>No directory is configured; the caller must rely on local credentials.</summary>
    Disabled,
    /// <summary>The directory accepted the username and password.</summary>
    Succeeded,
    /// <summary>The directory rejected the username and password.</summary>
    InvalidCredentials,
    /// <summary>The directory could not be reached or answered with an unexpected error.</summary>
    Unavailable
}

/// <summary>
/// Port for external credential verification. Implementations only answer whether the directory
/// accepts the credential; account registration, roles, and scope remain the local directory's.
/// </summary>
public interface IDirectoryAuthenticator
{
    Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken);
}

public sealed record AuthenticatedUser(StoredUserAccount Account, string IdentitySource);

public static class IdentitySources
{
    // Both values keep the "development" prefix on purpose: the login endpoint is Development-only
    // and HttpActorContext derives the actor's development flag from that prefix.
    public const string Local = "development-local";
    public const string ActiveDirectory = "development-active-directory";
}

public interface IUserDirectoryStore
{
    Task<IReadOnlyList<StoredUserAccount>> ListAsync(CancellationToken cancellationToken);
    Task<StoredUserAccount?> FindAsync(string subjectId, CancellationToken cancellationToken);
    Task<StoredUserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);
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
