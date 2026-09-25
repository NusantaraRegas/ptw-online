using System.Net;
using System.Net.Http.Json;
using Ptw.Contracts;

namespace Ptw.Api.IntegrationTests;

/// <summary>
/// Login journal, lockout visibility, and session revocation through the security stamp.
/// </summary>
[Collection(PtwApiTestGroup.Name)]
public sealed class SessionHardeningApiTests(PtwApiFactory factory)
{
    private const string LocalPassword = "LocalOnly12345";
    private const string DirectoryPassword = "Directory-Secret-98765";

    [Fact]
    public async Task EveryLoginAttemptIsJournaledWithPathAndOutcome()
    {
        var (userName, subjectId) = await CreateUserAsync();
        factory.DirectoryAuthenticator.Accept(userName, DirectoryPassword);
        using var client = factory.CreateClient();

        using var portal = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, DirectoryPassword));
        using var local = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword));
        using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, "Wrong-Password-1"));
        Assert.Equal(HttpStatusCode.NoContent, portal.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, local.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var admin = AdminClient($"auditor.{Guid.NewGuid():N}");
        var journal = await admin.GetFromJsonAsync<PagedResponse<LoginAuditEventResponse>>(
            $"/api/v1/admin/users/login-events?subjectId={Uri.EscapeDataString(subjectId)}&limit=10");
        Assert.NotNull(journal);
        var events = journal.Items.OrderBy(x => x.OccurredAt).ToArray();
        Assert.Equal(3, events.Length);
        Assert.All(events, x => Assert.Equal(subjectId, x.SubjectId));
        Assert.All(events, x => Assert.False(string.IsNullOrWhiteSpace(x.SourceAddress)));
        Assert.All(events, x => Assert.False(string.IsNullOrWhiteSpace(x.CorrelationId)));
        Assert.Equal("succeeded", events[0].Outcome);
        Assert.Equal("development-portal-api", events[0].IdentitySource);
        Assert.Equal("succeeded", events[0].DirectoryResult);
        Assert.Equal("succeeded", events[1].Outcome);
        Assert.Equal("development-local", events[1].IdentitySource);
        Assert.Equal("invalid_credentials", events[1].DirectoryResult);
        Assert.Equal("invalid_credentials", events[2].Outcome);
        Assert.Equal("none", events[2].IdentitySource);
        var serialized = System.Text.Json.JsonSerializer.Serialize(journal);
        Assert.DoesNotContain(DirectoryPassword, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalPassword, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginJournalIsAdministratorOnly()
    {
        using var sponsor = factory.CreateClient();
        sponsor.DefaultRequestHeaders.Add("X-Dev-User", $"sponsor.{Guid.NewGuid():N}");
        sponsor.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");

        using var response = await sponsor.GetAsync("/api/v1/admin/users/login-events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccountAndSurfaceItToAdministrators()
    {
        var (userName, subjectId) = await CreateUserAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, $"Wrong-Password-{attempt}"));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // The right password is refused while the lock holds, and the response is indistinguishable.
        using var lockedLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, lockedLogin.StatusCode);
        var problem = await lockedLogin.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        Assert.Equal("authentication.invalid_credentials", problem?.Code);

        using var admin = AdminClient($"watcher.{Guid.NewGuid():N}");
        var account = await admin.GetFromJsonAsync<UserAccountResponse>($"/api/v1/admin/users/{subjectId}");
        Assert.NotNull(account?.LockedUntil);
        Assert.True(account.LockedUntil > DateTimeOffset.UtcNow.AddMinutes(4));
        var journal = await admin.GetFromJsonAsync<PagedResponse<LoginAuditEventResponse>>(
            $"/api/v1/admin/users/login-events?subjectId={Uri.EscapeDataString(subjectId)}");
        Assert.NotNull(journal);
        Assert.Contains(journal.Items, x => x.Outcome == "lockout_triggered");
        Assert.Contains(journal.Items, x => x.Outcome == "locked_out");
    }

    [Fact]
    public async Task PasswordResetEndsExistingSessions()
    {
        var (userName, subjectId) = await CreateUserAsync();
        using var session = factory.CreateClient();
        using var login = await session.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var before = await session.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var admin = AdminClient($"resetter.{Guid.NewGuid():N}");
        var current = await admin.GetFromJsonAsync<UserAccountResponse>($"/api/v1/admin/users/{subjectId}");
        Assert.NotNull(current);
        using var reset = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/password")
        {
            Content = JsonContent.Create(new ResetUserPasswordRequest("Replacement-Passphrase-77"))
        };
        reset.Headers.TryAddWithoutValidation("If-Match", current.ETag);
        using var resetResponse = await admin.SendAsync(reset);
        resetResponse.EnsureSuccessStatusCode();

        using var after = await session.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanRevokeEverySessionOfAnAccount()
    {
        var (userName, subjectId) = await CreateUserAsync();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        (await first.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword))).EnsureSuccessStatusCode();
        (await second.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword))).EnsureSuccessStatusCode();

        using var admin = AdminClient($"revoker.{Guid.NewGuid():N}");
        var current = await admin.GetFromJsonAsync<UserAccountResponse>($"/api/v1/admin/users/{subjectId}");
        Assert.NotNull(current);
        using var revoke = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/sessions/revoke");
        revoke.Headers.TryAddWithoutValidation("If-Match", current.ETag);
        using var revoked = await admin.SendAsync(revoke);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var updated = await revoked.Content.ReadFromJsonAsync<UserAccountResponse>();
        Assert.Equal(current.Version + 1, updated?.Version);

        using var firstAfter = await first.GetAsync("/api/v1/me");
        using var secondAfter = await second.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, firstAfter.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAfter.StatusCode);

        // A stale ETag is a conflict, never a silent second revocation.
        using var stale = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/sessions/revoke");
        stale.Headers.TryAddWithoutValidation("If-Match", current.ETag);
        using var staleResponse = await admin.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        // A fresh login works again and carries the rotated stamp.
        using var again = factory.CreateClient();
        using var relogin = await again.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword));
        Assert.Equal(HttpStatusCode.NoContent, relogin.StatusCode);
        using var me = await again.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task DeactivationEndsExistingSessions()
    {
        var (userName, subjectId) = await CreateUserAsync();
        using var session = factory.CreateClient();
        (await session.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, LocalPassword))).EnsureSuccessStatusCode();

        using var admin = AdminClient($"deactivator.{Guid.NewGuid():N}");
        var current = await admin.GetFromJsonAsync<UserAccountResponse>($"/api/v1/admin/users/{subjectId}");
        Assert.NotNull(current);
        using var deactivate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/active")
        {
            Content = JsonContent.Create(new SetUserActiveRequest(false))
        };
        deactivate.Headers.TryAddWithoutValidation("If-Match", current.ETag);
        (await admin.SendAsync(deactivate)).EnsureSuccessStatusCode();

        using var after = await session.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Theory]
    [InlineData("Password2026!", "user.password_breached")]
    [InlineData("P@ssw0rd12345", "user.password_breached")]
    [InlineData("Pertamina#1234", "user.password_breached")]
    [InlineData("Welcome12345", "user.password_breached")]
    public async Task BreachedLocalPasswordsAreRefused(string password, string expectedCode)
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var admin = AdminClient($"creator.{suffix}");

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest($"weak.{suffix}", $"weak-{suffix}", "Pengguna", null, null, password));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        Assert.Equal(expectedCode, problem?.Code);
    }

    [Fact]
    public async Task PasswordContainingTheUsernameIsRefused()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var admin = AdminClient($"creator.{suffix}");
        var userName = $"budi{suffix}";

        using var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest($"budi.{suffix}", userName, "Budi", null, null, $"X{userName}Y9"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        Assert.Equal("user.password_contains_username", problem?.Code);
    }

    private async Task<(string UserName, string SubjectId)> CreateUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"session.user.{suffix}";
        var userName = $"session-{suffix}";
        using var admin = AdminClient($"creator.{suffix}");
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, userName, "Pengguna Sesi", "Staff", "TI", LocalPassword));
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

    private sealed record ProblemDetailsPayload(string? Code, string? Title);
}
