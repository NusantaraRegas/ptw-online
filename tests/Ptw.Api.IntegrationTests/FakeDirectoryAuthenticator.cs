using System.Collections.Concurrent;
using Ptw.Application;

namespace Ptw.Api.IntegrationTests;

/// <summary>
/// Replaces the LDAP bind in integration tests. Credentials are keyed per username so tests in the
/// shared collection can register their own users without interfering with each other.
/// </summary>
public sealed class FakeDirectoryAuthenticator : IDirectoryAuthenticator
{
    private readonly ConcurrentDictionary<string, string> _accepted = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _unavailable = new(StringComparer.OrdinalIgnoreCase);

    public void Accept(string userName, string password) => _accepted[userName.Trim()] = password;

    public void MarkUnavailable(string userName) => _unavailable[userName.Trim()] = 0;

    public Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var key = userName.Trim();
        if (_unavailable.ContainsKey(key))
        {
            return Task.FromResult(DirectoryAuthenticationResult.Unavailable);
        }

        return Task.FromResult(
            _accepted.TryGetValue(key, out var expected) && string.Equals(expected, password, StringComparison.Ordinal)
                ? DirectoryAuthenticationResult.Succeeded
                : DirectoryAuthenticationResult.InvalidCredentials);
    }
}
