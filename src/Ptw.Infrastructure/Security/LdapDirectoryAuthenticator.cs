using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Ptw.Application;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Verifies a username and password with a simple LDAP bind against Active Directory. The bind
/// identity is the user principal name (<c>username@domain</c>); nothing is read from the directory
/// and no connection is kept between calls, so the authenticator is safe as a singleton.
/// </summary>
internal sealed partial class LdapDirectoryAuthenticator(
    ActiveDirectorySettings settings,
    ILogger<LdapDirectoryAuthenticator> logger) : IDirectoryAuthenticator
{
    // LDAP resultCode 49 (invalidCredentials) is the only outcome that proves the password is wrong;
    // every other failure is treated as the directory being unavailable so local fallback applies.
    private const int InvalidCredentialsResultCode = 49;

    public async Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return DirectoryAuthenticationResult.Disabled;
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return DirectoryAuthenticationResult.InvalidCredentials;
        }

        var principalName = ToPrincipalName(userName.Trim());
        try
        {
            // LdapConnection.Bind is synchronous; run it off the request thread so the time-out and
            // cancellation token still bound the login request.
            return await Task.Run(() => Bind(principalName, password), cancellationToken);
        }
        catch (LdapException exception) when (exception.ErrorCode == InvalidCredentialsResultCode)
        {
            return DirectoryAuthenticationResult.InvalidCredentials;
        }
        catch (Exception exception) when (exception is LdapException
            or DirectoryOperationException
            or InvalidOperationException
            or TimeoutException
            or SocketException
            or PlatformNotSupportedException
            or DllNotFoundException
            or TypeInitializationException)
        {
            LogDirectoryUnavailable(logger, settings.Server, settings.Port, exception.GetType().Name, exception);
            return DirectoryAuthenticationResult.Unavailable;
        }
    }

    private DirectoryAuthenticationResult Bind(string principalName, string password)
    {
        var identifier = new LdapDirectoryIdentifier(
            settings.Server,
            settings.Port,
            fullyQualifiedDnsHostName: false,
            connectionless: false);
        using var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (settings.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else if (settings.UseStartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }
        else
        {
            LogClearTextBind(logger, settings.Server, settings.Port);
        }

        connection.Bind(new NetworkCredential(principalName, password));
        return DirectoryAuthenticationResult.Succeeded;
    }

    private string ToPrincipalName(string userName) =>
        userName.Contains('@', StringComparison.Ordinal) || userName.Contains('\\', StringComparison.Ordinal)
            ? userName
            : $"{userName}@{settings.Domain}";

    [LoggerMessage(
        EventId = 3100,
        EventName = "DirectoryUnavailable",
        Level = LogLevel.Warning,
        Message = "Active Directory bind failed; falling back to local credentials. Server={Server} Port={Port} Error={ErrorType}")]
    private static partial void LogDirectoryUnavailable(
        ILogger logger,
        string server,
        int port,
        string errorType,
        Exception exception);

    [LoggerMessage(
        EventId = 3101,
        EventName = "DirectoryClearTextBind",
        Level = LogLevel.Debug,
        Message = "Active Directory bind without StartTLS/LDAPS. Server={Server} Port={Port}")]
    private static partial void LogClearTextBind(ILogger logger, string server, int port);
}

/// <summary>Used when no directory is configured so login relies on local credentials only.</summary>
internal sealed class DisabledDirectoryAuthenticator : IDirectoryAuthenticator
{
    public Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken) => Task.FromResult(DirectoryAuthenticationResult.Disabled);
}
