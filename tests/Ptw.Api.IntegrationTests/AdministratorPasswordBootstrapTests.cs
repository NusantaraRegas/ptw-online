using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ptw.Api;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

/// <summary>
/// The out-of-band `--reset-password` command: only an active account with an approved
/// Administrator assignment can be bootstrapped, the password policy still applies, and the reset
/// ends existing sessions like an API reset would.
/// </summary>
[Collection(PtwApiTestGroup.Name)]
public sealed class AdministratorPasswordBootstrapTests(PtwApiFactory factory)
{
    private const string InitialPassword = "Initial-Passphrase-2026";
    private const string BootstrapPassword = "Recovered-Passphrase-77";

    [Fact]
    public async Task ResetsAnAdministratorPasswordAndEndsExistingSessions()
    {
        var (subjectId, userName) = await CreateAdministratorAsync();
        using var session = factory.CreateClient();
        (await session.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, InitialPassword))).EnsureSuccessStatusCode();

        var exitCode = await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services,
            NullLogger.Instance,
            subjectId,
            new StringReader(BootstrapPassword + "\n"));

        Assert.Equal(0, exitCode);
        using var staleSession = await session.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, staleSession.StatusCode);
        using var fresh = factory.CreateClient();
        using var oldPassword = await fresh.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, InitialPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await fresh.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, BootstrapPassword));
        Assert.Equal(HttpStatusCode.NoContent, newPassword.StatusCode);

        using var admin = AdminClient($"auditor.{Guid.NewGuid():N}");
        var journal = await admin.GetFromJsonAsync<PagedResponse<LoginAuditEventResponse>>(
            $"/api/v1/admin/users/login-events?subjectId={Uri.EscapeDataString(subjectId)}");
        Assert.NotNull(journal);
        Assert.Contains(journal.Items, x => x.Outcome == "succeeded" && x.IdentitySource == "development-local");
    }

    [Fact]
    public async Task CreatesTheFirstCredentialForASeededAdministrator()
    {
        var (subjectId, userName) = await CreateAdministratorAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var credential = await dbContext.UserCredentials.FindAsync(subjectId);
            Assert.NotNull(credential);
            dbContext.UserCredentials.Remove(credential);
            await dbContext.SaveChangesAsync();
        }

        var exitCode = await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services,
            NullLogger.Instance,
            subjectId,
            new StringReader(BootstrapPassword + "\n"));

        Assert.Equal(0, exitCode);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, BootstrapPassword));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task RefusesAccountsWithoutAnApprovedAdministratorAssignment()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"plain.user.{suffix}";
        using var admin = AdminClient($"creator.{suffix}");
        (await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, $"plain-{suffix}", "Pengguna Biasa", null, null, InitialPassword))).EnsureSuccessStatusCode();

        var exitCode = await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services,
            NullLogger.Instance,
            subjectId,
            new StringReader(BootstrapPassword + "\n"));

        Assert.Equal(1, exitCode);
        using var client = factory.CreateClient();
        using var unchanged = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest($"plain-{suffix}", InitialPassword));
        Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("Password2026!")]
    public async Task AppliesThePasswordPolicy(string weakPassword)
    {
        var (subjectId, userName) = await CreateAdministratorAsync();

        var exitCode = await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services,
            NullLogger.Instance,
            subjectId,
            new StringReader(weakPassword + "\n"));

        Assert.Equal(1, exitCode);
        using var client = factory.CreateClient();
        using var unchanged = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, InitialPassword));
        Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);
    }

    [Fact]
    public async Task RejectsMissingSubjectOrPasswordWithUsageExitCode()
    {
        Assert.Equal(2, await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services, NullLogger.Instance, "", new StringReader(BootstrapPassword + "\n")));
        Assert.Equal(2, await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services, NullLogger.Instance, "someone", new StringReader("")));
        Assert.Equal(1, await BootstrapCommand.ResetAdministratorPasswordAsync(
            factory.Services, NullLogger.Instance, $"missing.{Guid.NewGuid():N}", new StringReader(BootstrapPassword + "\n")));
    }

    private async Task<(string SubjectId, string UserName)> CreateAdministratorAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"bootstrap.admin.{suffix}";
        var userName = $"bootstrap-{suffix}";
        using var maker = AdminClient($"maker.{suffix}");
        (await maker.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, userName, "Break-glass Administrator", "Administrator", "TI", InitialPassword))).EnsureSuccessStatusCode();
        using var draftResponse = await maker.PostAsJsonAsync(
            "/api/v1/admin/authorizations",
            new UserAuthorizationDraftRequest(
                subjectId,
                "Administrator",
                ["admin.manage"],
                null,
                false,
                [],
                "DIRECT",
                null,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(30)));
        draftResponse.EnsureSuccessStatusCode();
        var draft = Assert.IsType<UserAuthorizationResponse>(await draftResponse.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
        var pending = await CommandAsync(maker, draft, "submit");
        using var checker = AdminClient($"checker.{suffix}");
        _ = await CommandAsync(checker, pending, "approve");
        return (subjectId, userName);
    }

    private static async Task<UserAuthorizationResponse> CommandAsync(
        HttpClient client,
        UserAuthorizationResponse assignment,
        string command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/authorizations/{assignment.Id}/{command}");
        request.Headers.TryAddWithoutValidation("If-Match", assignment.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return Assert.IsType<UserAuthorizationResponse>(await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
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
