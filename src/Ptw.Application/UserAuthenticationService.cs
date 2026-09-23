namespace Ptw.Application;

public sealed class UserAuthenticationService(
    IUserDirectoryStore store,
    IDirectoryAuthenticator directoryAuthenticator)
{
    /// <summary>
    /// Verifies the credential against Active Directory first and falls back to the local
    /// password store. Either path requires a registered, active local account: the directory
    /// only proves the password, it never provisions users, roles, or scope.
    /// </summary>
    public async Task<AuthenticatedUser> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            throw new AuthenticationFailedException();
        }

        var directoryResult = await directoryAuthenticator.AuthenticateAsync(userName, password, cancellationToken);
        if (directoryResult == DirectoryAuthenticationResult.Succeeded)
        {
            var registered = await store.FindByUserNameAsync(userName, cancellationToken);
            if (registered is { Account.IsActive: true })
            {
                return new AuthenticatedUser(registered, IdentitySources.ActiveDirectory);
            }
        }

        // Directory rejection, outage, disabled directory, or an unregistered directory user all
        // fall through to the local credential so registered local accounts keep working.
        var local = await store.AuthenticateAsync(userName, password, cancellationToken)
            ?? throw new AuthenticationFailedException();
        return new AuthenticatedUser(local, IdentitySources.Local);
    }
}
