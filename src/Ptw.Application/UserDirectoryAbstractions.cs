using Ptw.Domain;

namespace Ptw.Application;

public sealed record StoredUserAccount(
    UserAccount Account,
    string ETag,
    UserSignatureEntry? Signature,
    DateTimeOffset? LockedUntil = null);

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
    string SecurityStamp,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> LocationScopes,
    IReadOnlySet<string> CompetencyCodes);

/// <summary>Why the local credential store accepted or refused a login attempt.</summary>
public enum LocalAuthenticationOutcome
{
    Succeeded,
    UnknownUser,
    AccountInactive,
    /// <summary>The account is currently locked; the password was not evaluated.</summary>
    LockedOut,
    InvalidPassword,
    /// <summary>The wrong password reached the failure threshold and locked the account.</summary>
    LockoutTriggered
}

public sealed record LocalAuthenticationResult(
    LocalAuthenticationOutcome Outcome,
    StoredUserAccount? Account,
    DateTimeOffset? LockedUntil);

/// <summary>
/// One row of the append-only login journal (who, from where, by which path, with what result).
/// The password is never part of it.
/// </summary>
public sealed record LoginAuditEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    string UserName,
    string? SubjectId,
    string DirectoryResult,
    string IdentitySource,
    string Outcome,
    string SourceAddress,
    string CorrelationId);

public static class LoginOutcomes
{
    public const string Succeeded = "succeeded";
    public const string InvalidCredentials = "invalid_credentials";
    public const string Unregistered = "unregistered";
    public const string AccountInactive = "account_inactive";
    public const string LockedOut = "locked_out";
    public const string LockoutTriggered = "lockout_triggered";
}

public interface ILoginAuditStore
{
    Task RecordAsync(LoginAuditEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<LoginAuditEntry>> ListRecentAsync(
        int limit,
        string? subjectId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Port for checking a candidate local password against known-breached passwords. Implementations
/// must never send the clear-text password anywhere (k-anonymity or an offline list only).
/// </summary>
public interface IBreachedPasswordChecker
{
    /// <exception cref="BreachedPasswordCheckUnavailableException">The verdict could not be obtained; callers must refuse the password.</exception>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken);
}

public sealed class BreachedPasswordCheckUnavailableException()
    : Exception("Pemeriksaan kebocoran password tidak tersedia; coba lagi nanti.");

/// <summary>
/// Outcome of verifying a credential against an external directory (the Portal API, which in turn
/// binds to Active Directory).
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
    public const string PortalApi = "development-portal-api";
    /// <summary>Recorded on a failed attempt, when no identity was established.</summary>
    public const string None = "none";
}

public interface IUserDirectoryStore
{
    Task<IReadOnlyList<StoredUserAccount>> ListAsync(CancellationToken cancellationToken);
    Task<StoredUserAccount?> FindAsync(string subjectId, CancellationToken cancellationToken);
    Task<StoredUserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);
    Task<LocalAuthenticationResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
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
    /// <summary>Persists a rotated security stamp so every live session of the account ends.</summary>
    Task<StoredUserAccount> RevokeSessionsAsync(
        UserAccount account,
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
