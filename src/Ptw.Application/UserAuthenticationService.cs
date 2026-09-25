namespace Ptw.Application;

public sealed class UserAuthenticationService(
    IUserDirectoryStore store,
    IDirectoryAuthenticator directoryAuthenticator,
    ILoginAuditStore loginAudit,
    IClock clock)
{
    /// <summary>
    /// Verifies the credential through the Portal API (which binds to Active Directory) first and
    /// falls back to the local password store. Either path requires a registered, active local
    /// account: the portal only proves the password, it never provisions users, roles, or scope.
    /// Every attempt, successful or not, is journaled with its source address and path so a login
    /// is evidence rather than an untraceable event.
    /// </summary>
    public async Task<AuthenticatedUser> AuthenticateAsync(
        string userName,
        string password,
        string sourceAddress,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var attemptedUserName = userName.Trim();
        if (attemptedUserName.Length == 0 || string.IsNullOrEmpty(password))
        {
            await RecordAsync(attemptedUserName, null, "not_attempted", IdentitySources.None, LoginOutcomes.InvalidCredentials, sourceAddress, correlationId, cancellationToken);
            throw new AuthenticationFailedException();
        }

        var directoryResult = await directoryAuthenticator.AuthenticateAsync(attemptedUserName, password, cancellationToken);
        var directoryLabel = DirectoryLabel(directoryResult);
        if (directoryResult == DirectoryAuthenticationResult.Succeeded)
        {
            var registered = await store.FindByUserNameAsync(attemptedUserName, cancellationToken);
            if (registered is { Account.IsActive: true })
            {
                await RecordAsync(attemptedUserName, registered.Account.SubjectId, directoryLabel, IdentitySources.PortalApi, LoginOutcomes.Succeeded, sourceAddress, correlationId, cancellationToken);
                return new AuthenticatedUser(registered, IdentitySources.PortalApi);
            }

            // The portal vouched for the password, so the local store would only reveal the same
            // registration problem. Journal the precise reason and stop here.
            var outcome = registered is null ? LoginOutcomes.Unregistered : LoginOutcomes.AccountInactive;
            await RecordAsync(attemptedUserName, registered?.Account.SubjectId, directoryLabel, IdentitySources.None, outcome, sourceAddress, correlationId, cancellationToken);
            throw new AuthenticationFailedException();
        }

        // Portal rejection, outage, or a disabled portal all fall through to the local credential so
        // registered local (break-glass) accounts keep working.
        var local = await store.AuthenticateAsync(attemptedUserName, password, cancellationToken);
        if (local.Outcome == LocalAuthenticationOutcome.Succeeded && local.Account is not null)
        {
            await RecordAsync(attemptedUserName, local.Account.Account.SubjectId, directoryLabel, IdentitySources.Local, LoginOutcomes.Succeeded, sourceAddress, correlationId, cancellationToken);
            return new AuthenticatedUser(local.Account, IdentitySources.Local);
        }

        await RecordAsync(
            attemptedUserName,
            local.Account?.Account.SubjectId,
            directoryLabel,
            IdentitySources.None,
            LocalOutcome(local.Outcome),
            sourceAddress,
            correlationId,
            cancellationToken);
        throw local.Outcome is LocalAuthenticationOutcome.LockedOut or LocalAuthenticationOutcome.LockoutTriggered
            ? new AccountLockedOutException(attemptedUserName, sourceAddress, local.LockedUntil)
            : new AuthenticationFailedException();
    }

    private Task RecordAsync(
        string userName,
        string? subjectId,
        string directoryResult,
        string identitySource,
        string outcome,
        string sourceAddress,
        string correlationId,
        CancellationToken cancellationToken) =>
        loginAudit.RecordAsync(
            new LoginAuditEntry(
                Guid.CreateVersion7(),
                clock.UtcNow,
                userName.Length > 100 ? userName[..100] : userName,
                subjectId,
                directoryResult,
                identitySource,
                outcome,
                string.IsNullOrWhiteSpace(sourceAddress) ? "unknown" : sourceAddress,
                correlationId),
            cancellationToken);

    private static string DirectoryLabel(DirectoryAuthenticationResult result) => result switch
    {
        DirectoryAuthenticationResult.Succeeded => "succeeded",
        DirectoryAuthenticationResult.InvalidCredentials => "invalid_credentials",
        DirectoryAuthenticationResult.Unavailable => "unavailable",
        _ => "disabled"
    };

    private static string LocalOutcome(LocalAuthenticationOutcome outcome) => outcome switch
    {
        LocalAuthenticationOutcome.LockedOut => LoginOutcomes.LockedOut,
        LocalAuthenticationOutcome.LockoutTriggered => LoginOutcomes.LockoutTriggered,
        LocalAuthenticationOutcome.AccountInactive => LoginOutcomes.AccountInactive,
        LocalAuthenticationOutcome.UnknownUser => LoginOutcomes.Unregistered,
        _ => LoginOutcomes.InvalidCredentials
    };
}

/// <summary>
/// A login attempt hit or extended a lockout. It maps to the same 401 as any other failure so the
/// response does not reveal account state; the details exist for the operator log only.
/// </summary>
public sealed class AccountLockedOutException(string userName, string sourceAddress, DateTimeOffset? lockedUntil)
    : Exception("Username atau password tidak valid.")
{
    public string UserName { get; } = userName;
    public string SourceAddress { get; } = sourceAddress;
    public DateTimeOffset? LockedUntil { get; } = lockedUntil;
}
