namespace Ptw.Application;

public sealed class UserAuthenticationService(IUserDirectoryStore store)
{
    public async Task<StoredUserAccount> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            throw new AuthenticationFailedException();
        }

        return await store.AuthenticateAsync(userName, password, cancellationToken)
            ?? throw new AuthenticationFailedException();
    }
}
