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
        TimeoutSeconds = 5
    };

    [Fact]
    public async Task PostsPortalContractAndSucceedsOnToken()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"token":"eyJhbGciOiJIUzI1NiJ9.x.y"}"""));

        var result = await Create(handler).AuthenticateAsync(" john.doe ", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Succeeded, result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://local.api.portal.com/api/v1/User/SecureAuth"), request.Uri);
        Assert.Equal("application/json", request.MediaType);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("john.doe", body.RootElement.GetProperty("UserName").GetString());
        Assert.Equal("Secret-1", body.RootElement.GetProperty("Password").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task UnauthorizedMeansInvalidCredentials()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Invalid credentials")
        });

        var result = await Create(handler).AuthenticateAsync("john.doe", "wrong", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.InvalidCredentials, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task OtherStatusCodesMeanUnavailable(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode));

        var result = await Create(handler).AuthenticateAsync("john.doe", "Secret-1", CancellationToken.None);

        Assert.Equal(DirectoryAuthenticationResult.Unavailable, result);
    }

    [Theory]
    [InlineData("""{"token":""}""")]
    [InlineData("""{"other":"value"}""")]
    [InlineData("")]
    [InlineData("not json")]
    public async Task SuccessWithoutTokenMeansUnavailable(string body)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));

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

    private static PortalApiDirectoryAuthenticator Create(StubHandler handler) =>
        new(new HttpClient(handler), Settings, NullLogger<PortalApiDirectoryAuthenticator>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? MediaType, string Body);

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
                body);
            Requests.Add(captured);
            return respond(captured);
        }
    }
}
