using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ptw.Application;
using Ptw.Infrastructure.Security;

namespace Ptw.Api.IntegrationTests;

public sealed class PortalApiDirectoryAuthenticatorTests
{
    private static readonly PortalAuthenticationSettings Settings = new()
    {
        Enabled = true,
        BaseUrl = new Uri("http://local.api.portal.com/"),
        ServiceUserName = "portal-service",
        ServicePassword = "Service-Secret",
        TimeoutSeconds = 5
    };

    [Fact]
    public async Task ObtainsServiceTokenThenPostsBearerProtectedDirectoryContract()
    {
        var handler = new StubHandler(request => request.Uri?.AbsolutePath switch
        {
            "/api/v1/User/SecureAuth" => Json(HttpStatusCode.OK, """{"token":"service.jwt.token"}"""),
            "/api/v1/User/ADAuth" => Json(HttpStatusCode.OK, "true"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await Create(handler).AuthenticateAsync(
            " john.doe ",
            "Secret@1\\value",
            CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Succeeded, result);
        Assert.Equal(2, handler.Requests.Count);

        var serviceRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, serviceRequest.Method);
        Assert.Equal(new Uri("http://local.api.portal.com/api/v1/User/SecureAuth"), serviceRequest.Uri);
        Assert.Equal("application/json", serviceRequest.MediaType);
        Assert.Null(serviceRequest.AuthorizationScheme);
        using (var body = JsonDocument.Parse(serviceRequest.Body))
        {
            Assert.Equal("portal-service", body.RootElement.GetProperty("UserName").GetString());
            Assert.Equal("Service-Secret", body.RootElement.GetProperty("Password").GetString());
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        }

        var directoryRequest = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, directoryRequest.Method);
        Assert.Equal("/api/v1/User/ADAuth", directoryRequest.Uri?.AbsolutePath);
        Assert.Equal("?userName=john.doe&password=Secret%401%5Cvalue", directoryRequest.Uri?.Query);
        Assert.Equal("Bearer", directoryRequest.AuthorizationScheme);
        Assert.Equal("service.jwt.token", directoryRequest.AuthorizationParameter);
        Assert.Equal(0, directoryRequest.ContentLength);
        Assert.Equal(string.Empty, directoryRequest.Body);
    }

    [Fact]
    public async Task DirectoryFalseMeansInvalidCredentials()
    {
        var handler = TokenThen(Json(HttpStatusCode.OK, "false"));

        var result = await Create(handler).AuthenticateAsync("john.doe", "wrong", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.InvalidCredentials, result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ServiceAuthenticationFailureMeansUnavailable(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DirectoryUnexpectedStatusMeansUnavailable(HttpStatusCode statusCode)
    {
        var handler = TokenThen(new HttpResponseMessage(statusCode));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("""{"token":""}""")]
    [InlineData("""{"other":"value"}""")]
    [InlineData("")]
    [InlineData("not json")]
    public async Task ServiceSuccessWithoutTokenMeansUnavailable(string body)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task InvalidDirectoryResponseMeansUnavailable(string body)
    {
        var handler = TokenThen(Json(HttpStatusCode.OK, body));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
    }

    [Fact]
    public async Task ConnectionFailureMeansUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
    }

    [Fact]
    public async Task TimeoutMeansUnavailable()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(handler).AuthenticateAsync("john.doe", "Secret-1", cancellation.Token));
    }

    [Theory]
    [InlineData("", "Secret-1")]
    [InlineData("   ", "Secret-1")]
    [InlineData("john.doe", "")]
    public async Task BlankCredentialIsRejectedWithoutCallingThePortal(string userName, string password)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"token":"x"}"""));

        var result = await Create(handler).AuthenticateAsync(userName, password, CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.InvalidCredentials, result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DisabledSettingsSkipThePortal()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"token":"x"}"""));
        var authenticator = new PortalApiDirectoryAuthenticator(
            new HttpClient(handler),
            new PortalAuthenticationSettings { Enabled = false },
            NullLogger<PortalApiDirectoryAuthenticator>.Instance);

        var result = await authenticator.AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Disabled, result);
        Assert.Empty(handler.Requests);
    }

    private static StubHandler TokenThen(HttpResponseMessage directoryResponse)
    {
        var requestNumber = 0;
        return new StubHandler(_ => ++requestNumber == 1
            ? Json(HttpStatusCode.OK, """{"token":"service.jwt.token"}""")
            : directoryResponse);
    }

    private static PortalApiDirectoryAuthenticator Create(StubHandler handler) =>
        new(new HttpClient(handler), Settings, NullLogger<PortalApiDirectoryAuthenticator>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        string? MediaType,
        string Body,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        long? ContentLength);

    private sealed class StubHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Content?.Headers.ContentType?.MediaType,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content?.Headers.ContentLength);
            Requests.Add(captured);
            return respond(captured);
        }
    }
}
