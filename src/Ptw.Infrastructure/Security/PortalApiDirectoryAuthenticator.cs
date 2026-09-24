using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Ptw.Application;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Verifies a username and password by calling the Portal API login endpoint
/// (<c>POST /api/v1/User/SecureAuth</c>). The portal answers 200 with a JWT when the credential is
/// valid and 401 when it is not; the JWT is discarded because the local <c>UserAccount</c> and its
/// approved assignments remain the only source of identity, roles, and scope.
/// </summary>
public sealed partial class PortalApiDirectoryAuthenticator(
    HttpClient httpClient,
    PortalAuthenticationSettings settings,
    ILogger<PortalApiDirectoryAuthenticator> logger) : IDirectoryAuthenticator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled || settings.AuthenticateEndpoint is null)
        {
            return DirectoryAuthenticationResult.Disabled;
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return DirectoryAuthenticationResult.InvalidCredentials;
        }

        var endpoint = settings.AuthenticateEndpoint;
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                endpoint,
                new SecureAuthRequest(userName.Trim(), password),
                SerializerOptions,
                cancellationToken);
            // 401 is the only answer that proves the password is wrong; every other failure is treated
            // as the portal being unavailable so the local password fallback still applies.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return DirectoryAuthenticationResult.InvalidCredentials;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedResponse(logger, endpoint.Host, (int)response.StatusCode);
                return DirectoryAuthenticationResult.Unavailable;
            }

            var payload = await response.Content.ReadFromJsonAsync<SecureAuthResponse>(
                SerializerOptions,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(payload?.Token))
            {
                LogUnexpectedResponse(logger, endpoint.Host, (int)response.StatusCode);
                return DirectoryAuthenticationResult.Unavailable;
            }

            return DirectoryAuthenticationResult.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or OperationCanceledException
            or JsonException
            or InvalidOperationException)
        {
            LogPortalUnavailable(logger, endpoint.Host, exception.GetType().Name, exception);
            return DirectoryAuthenticationResult.Unavailable;
        }
    }

    // Property names follow the Portal API contract (`UserName`, `Password`) verbatim.
    private sealed record SecureAuthRequest(
        [property: JsonPropertyName("UserName")] string UserName,
        [property: JsonPropertyName("Password")] string Password);

    private sealed record SecureAuthResponse([property: JsonPropertyName("token")] string? Token);

    [LoggerMessage(
        EventId = 3100,
        EventName = "PortalAuthenticationUnavailable",
        Level = LogLevel.Warning,
        Message = "Portal API login call failed; falling back to local credentials. Host={Host} Error={ErrorType}")]
    private static partial void LogPortalUnavailable(
        ILogger logger,
        string host,
        string errorType,
        Exception exception);

    [LoggerMessage(
        EventId = 3101,
        EventName = "PortalAuthenticationUnexpectedResponse",
        Level = LogLevel.Warning,
        Message = "Portal API login returned an unexpected response; falling back to local credentials. Host={Host} Status={StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, string host, int statusCode);
}

/// <summary>Used when no portal is configured so login relies on local credentials only.</summary>
internal sealed class DisabledDirectoryAuthenticator : IDirectoryAuthenticator
{
    public Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken) => Task.FromResult(DirectoryAuthenticationResult.Disabled);
}
