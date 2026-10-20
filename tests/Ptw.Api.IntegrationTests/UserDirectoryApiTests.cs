using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Domain;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class UserDirectoryApiTests(PtwApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task AdministratorCanCreateUserUploadSignatureAndLoginWithEffectiveRole()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"local.admin.{suffix}";
        var userName = $"local-{suffix}";
        const string password = "Development12345";
        using var maker = AdminClient($"maker.{suffix}");
        using var createResponse = await maker.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, userName, "Local Administrator", "Administrator", "TI", password));
        createResponse.EnsureSuccessStatusCode();
        var user = Assert.IsType<UserAccountResponse>(
            await createResponse.Content.ReadFromJsonAsync<UserAccountResponse>());

        using var signatureContent = new MultipartFormDataContent();
        using var image = new ByteArrayContent(Png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        signatureContent.Add(image, "file", "signature.png");
        using var signatureRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/users/{subjectId}/signature")
        {
            Content = signatureContent
        };
        signatureRequest.Headers.TryAddWithoutValidation("If-Match", user.ETag);
        using var signatureResponse = await maker.SendAsync(signatureRequest);
        signatureResponse.EnsureSuccessStatusCode();
        var signedUser = Assert.IsType<UserAccountResponse>(
            await signatureResponse.Content.ReadFromJsonAsync<UserAccountResponse>());
        Assert.NotNull(signedUser.Signature);
        Assert.Equal(1, signedUser.Signature.Version);

        using var signatureDownload = await maker.GetAsync($"/api/v1/admin/users/{subjectId}/signature");
        signatureDownload.EnsureSuccessStatusCode();
        Assert.Equal("image/png", signatureDownload.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await signatureDownload.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", signatureDownload.Headers.CacheControl?.ToString());

        var assignment = await maker.PostAsJsonAsync(
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
        assignment.EnsureSuccessStatusCode();
        var draft = Assert.IsType<UserAuthorizationResponse>(
            await assignment.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
        var pending = await CommandAsync(maker, draft, "submit");
        using var checker = AdminClient($"checker.{suffix}");
        _ = await CommandAsync(checker, pending, "approve");

        using var local = factory.CreateClient();
        using var login = await local.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, password));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var meResponse = await local.GetAsync("/api/v1/me");
        meResponse.EnsureSuccessStatusCode();
        var me = Assert.IsType<MeResponse>(await meResponse.Content.ReadFromJsonAsync<MeResponse>());
        Assert.Equal(subjectId, me.UserId);
        Assert.Contains("Administrator", me.Roles);
        Assert.Contains("*", me.LocationScopes);
        Assert.False(me.IsDevelopmentIdentity);
    }

    [Fact]
    public async Task SignatureUploadRejectsNonPngContent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var admin = AdminClient($"admin.invalid-signature.{suffix}");
        using var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(
                $"invalid.signature.{suffix}",
                $"invalid-{suffix}",
                "Invalid Signature",
                null,
                null,
                "Development12345"));
        createResponse.EnsureSuccessStatusCode();
        var user = Assert.IsType<UserAccountResponse>(
            await createResponse.Content.ReadFromJsonAsync<UserAccountResponse>());

        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent("not a png"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", "fake.png");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/users/{user.SubjectId}/signature")
        { Content = form };
        request.Headers.TryAddWithoutValidation("If-Match", user.ETag);
        using var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PasswordResetBootstrapsASeededAccountThatHasNoLocalCredentialYet()
    {
        // Accounts seeded by migration (e.g. superadmin.local) carry no credential row; the
        // documented bootstrap is the admin password reset followed by a local login.
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"seeded.{suffix}";
        var userName = $"seeded-{suffix}";
        const string password = "Bootstrap-Password-123!";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.UserAccounts.Add(new UserAccountRecord
            {
                SubjectId = subjectId,
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                DisplayName = "Seeded Administrator",
                Position = "System Administrator",
                Department = "IT",
                IsActive = true,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
                SecurityStamp = UserAccount.NewSecurityStamp()
            });
            await db.SaveChangesAsync();
        }

        using var admin = AdminClient($"admin.{suffix}");
        using var account = await admin.GetAsync($"/api/v1/admin/users/{subjectId}");
        account.EnsureSuccessStatusCode();
        var etag = account.Headers.ETag?.ToString();
        using var reset = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/password")
        {
            Content = JsonContent.Create(new ResetUserPasswordRequest(password))
        };
        reset.Headers.TryAddWithoutValidation("If-Match", etag);
        reset.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var resetResponse = await admin.SendAsync(reset);
        Assert.True(resetResponse.IsSuccessStatusCode, await resetResponse.Content.ReadAsStringAsync());

        using var local = factory.CreateClient();
        using var login = await local.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, password));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private HttpClient AdminClient(string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");
        return client;
    }

    private static async Task<UserAuthorizationResponse> CommandAsync(
        HttpClient client,
        UserAuthorizationResponse assignment,
        string command)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/authorizations/{assignment.Id}/{command}");
        request.Headers.TryAddWithoutValidation("If-Match", assignment.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return Assert.IsType<UserAuthorizationResponse>(
            await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
    }
}
