using System.Net;
using System.Net.Http.Json;
using Ptw.Contracts;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class DirectoryLoginApiTests(PtwApiFactory factory)
{
    private const string LocalPassword = "LocalOnly12345";
    private const string DirectoryPassword = "Directory-Secret-98765";

    [Fact]
    public async Task RegisteredUserAcceptedByDirectoryLogsInWithoutLocalPassword()
    {
        var (userName, subjectId) = await CreateUserAsync();
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, DirectoryPassword));

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.NotNull(me);
        Assert.Equal(subjectId, me.UserId);
    }

    [Fact]
    public async Task DirectoryUserWithoutRegisteredAccountIsRejected()
    {
        var userName = $"unregistered-{Guid.NewGuid():N}";
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, DirectoryPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task InactiveAccountAcceptedByDirectoryIsRejected()
    {
        var (userName, subjectId) = await CreateUserAsync();
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);
        using var admin = AdminClient($"deactivator.{Guid.NewGuid():N}");
        var current = await admin.GetFromJsonAsync<UserAccountResponse>($"/api/v1/admin/users/{subjectId}");
        Assert.NotNull(current);
        using var deactivate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/active")
        {
            Content = JsonContent.Create(new SetUserActiveRequest(false))
        };
        deactivate.Headers.TryAddWithoutValidation("If-Match", current.ETag);
        using var deactivated = await admin.SendAsync(deactivate);
        deactivated.EnsureSuccessStatusCode();

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, DirectoryPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task DirectoryRejectionFallsBackToLocalPassword()
    {
        var (userName, subjectId) = await CreateUserAsync();
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword));

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(subjectId, me?.UserId);
    }

    [Fact]
    public async Task DirectoryOutageFallsBackToLocalPassword()
    {
        var (userName, _) = await CreateUserAsync();
        factory.DirectoryAuthenticator.MarkUnavailable(userName);

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword));

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordOnBothDirectoryAndLocalIsRejected()
    {
        var (userName, _) = await CreateUserAsync();
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, "Neither-Password-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(login.Headers.TryGetValues("Set-Cookie", out _), "Login yang gagal tidak boleh membuat cookie sesi.");
    }

    private async Task<(string UserName, string SubjectId)> CreateUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"ad.user.{suffix}";
        var userName = $"ad-{suffix}";
        using var admin = AdminClient($"creator.{suffix}");
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, userName, "Pengguna Direktori", "Staff", "TI", LocalPassword));
        response.EnsureSuccessStatusCode();
        return (userName, subjectId);
    }

    private HttpClient AdminClient(string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");
        return client;
    }
}
