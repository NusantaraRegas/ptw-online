using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Ptw.Application;

namespace Ptw.Infrastructure.Security;

/// <summary>
/// Verifies a username and password through the Portal API's two-step contract. A service credential
/// obtains a short-lived bearer token from <c>SecureAuth</c>; that token authorizes the employee
/// credential check at <c>ADAuth</c>. The token is never used as PTW identity authority or persisted.
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
        if (!settings.Enabled
            || settings.AuthenticateEndpoint is null
            || settings.DirectoryAuthenticateEndpoint is null)
        {
            return DirectoryAuthenticationResult.Disabled;
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return DirectoryAuthenticationResult.InvalidCredentials;
        }

        try
        {
            var token = await AcquireServiceTokenAsync(cancellationToken);
            if (token is null)
            {
                return DirectoryAuthenticationResult.Unavailable;
            }

            var endpoint = settings.DirectoryAuthenticateEndpoint;
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                DirectoryAuthenticationUri(endpoint, userName.Trim(), password));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // The Portal endpoint requires POST with query parameters and an explicit empty body.
            request.Content = new ByteArrayContent([]);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedResponse(logger, "directory-authentication", endpoint.Host, (int)response.StatusCode);
                return DirectoryAuthenticationResult.Unavailable;
            }

            var accepted = await response.Content.ReadFromJsonAsync<bool>(
                SerializerOptions,
                cancellationToken);
            return accepted
                ? DirectoryAuthenticationResult.Succeeded
                : DirectoryAuthenticationResult.InvalidCredentials;
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
            LogPortalUnavailable(
                logger,
                settings.BaseUrl?.Host ?? "unconfigured",
                exception.GetType().Name,
                null);
            return DirectoryAuthenticationResult.Unavailable;
        }
    }

    private async Task<string?> AcquireServiceTokenAsync(CancellationToken cancellationToken)
    {
        var endpoint = settings.AuthenticateEndpoint!;
        using var response = await httpClient.PostAsJsonAsync(
            endpoint,
            new SecureAuthRequest(settings.ServiceUserName, settings.ServicePassword),
            SerializerOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogUnexpectedResponse(logger, "service-authentication", endpoint.Host, (int)response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<SecureAuthResponse>(
            SerializerOptions,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(payload?.Token))
        {
            return payload.Token;
        }

        LogUnexpectedResponse(logger, "service-authentication", endpoint.Host, (int)response.StatusCode);
        return null;
    }

    private static Uri DirectoryAuthenticationUri(Uri endpoint, string userName, string password)
    {
        var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        return new Uri(
            endpoint.AbsoluteUri
            + separator
            + "userName="
            + Uri.EscapeDataString(userName)
            + "&password="
            + Uri.EscapeDataString(password));
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
        Exception? exception);

    [LoggerMessage(
        EventId = 3101,
        EventName = "PortalAuthenticationUnexpectedResponse",
        Level = LogLevel.Warning,
        Message = "Portal API login returned an unexpected response; falling back to local credentials. Stage={Stage} Host={Host} Status={StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, string stage, string host, int statusCode);
}

/// <summary>Used when no portal is configured so login relies on local credentials only.</summary>
internal sealed class DisabledDirectoryAuthenticator : IDirectoryAuthenticator
{
    public Task<DirectoryAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken) => Task.FromResult(DirectoryAuthenticationResult.Disabled);
}
