using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class UserGuideApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task EveryAuthenticatedRoleCanDownloadAndOnlyAdministratorCanReplaceThePdf()
    {
        // No guide ships with the build, so before the first Administrator upload the API must
        // report an explicit "not available" state and refuse the download with 404.
        using var sponsor = Client("guide.sponsor", "Sponsor");
        var initial = await sponsor.GetFromJsonAsync<UserGuideResponse>("/api/v1/user-guide");
        Assert.NotNull(initial);
        Assert.False(initial.Available);
        Assert.Null(initial.FileName);
        Assert.Equal(0, initial.Version);
        Assert.Equal("\"0\"", initial.ETag);

        using var missingDownload = await sponsor.GetAsync("/api/v1/user-guide/content");
        Assert.Equal(HttpStatusCode.NotFound, missingDownload.StatusCode);

        using var denied = UploadRequest(
            initial.ETag,
            Guid.NewGuid().ToString("N"),
            "panduan-ditolak.pdf",
            "%PDF-1.7\nnot-authorized");
        using var deniedResponse = await sponsor.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var admin = Client("guide.admin", "Administrator");
        var key = Guid.NewGuid().ToString("N");
        using var upload = UploadRequest(initial.ETag, key, "Panduan-PTW-v2.pdf", "%PDF-1.7\nversion-two");
        using var uploadResponse = await admin.SendAsync(upload);
        uploadResponse.EnsureSuccessStatusCode();
        var updated = await uploadResponse.Content.ReadFromJsonAsync<UserGuideResponse>();
        Assert.NotNull(updated);
        Assert.True(updated.Available);
        Assert.Equal(initial.Version + 1, updated.Version);
        Assert.Equal("Panduan-PTW-v2.pdf", updated.FileName);

        var published = await sponsor.GetFromJsonAsync<UserGuideResponse>("/api/v1/user-guide");
        Assert.NotNull(published);
        Assert.True(published.Available);
        Assert.Equal(updated.ETag, published.ETag);

        using var auditor = Client("guide.auditor", "Auditor");
        using var auditorDownload = await auditor.GetAsync("/api/v1/user-guide/content");
        auditorDownload.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", auditorDownload.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", auditorDownload.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store, max-age=0", auditorDownload.Headers.CacheControl?.ToString());
        Assert.Equal("%PDF-1.7\nversion-two", await auditorDownload.Content.ReadAsStringAsync());

        using var replay = UploadRequest(initial.ETag, key, "Panduan-PTW-v2.pdf", "%PDF-1.7\nversion-two");
        using var replayResponse = await admin.SendAsync(replay);
        replayResponse.EnsureSuccessStatusCode();
        var replayed = await replayResponse.Content.ReadFromJsonAsync<UserGuideResponse>();
        Assert.Equal(updated, replayed);

        using var mismatch = UploadRequest(
            updated.ETag,
            key,
            "Panduan-PTW-v3.pdf",
            "%PDF-1.7\ndifferent");
        using var mismatchResponse = await admin.SendAsync(mismatch);
        Assert.Equal(HttpStatusCode.Conflict, mismatchResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal(1, await db.ConfigurationAuditEvents.CountAsync(
            x => x.AggregateType == "UserGuideSetting" && x.ActorId == "guide.admin"));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(
            x => x.EventType == "user_guide_replaced" && x.PayloadJson.Contains("Panduan-PTW-v2.pdf")));
    }

    [Fact]
    public async Task AdministratorUploadRejectsNonPdfSignatureAndStaleVersion()
    {
        using var admin = Client("guide.validation-admin", "Administrator");
        var current = await admin.GetFromJsonAsync<UserGuideResponse>("/api/v1/user-guide");
        Assert.NotNull(current);

        using var invalid = UploadRequest(
            current.ETag,
            Guid.NewGuid().ToString("N"),
            "panduan.pdf",
            "not-a-pdf");
        using var invalidResponse = await admin.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResponse.StatusCode);

        using var stale = UploadRequest(
            "\"999999\"",
            Guid.NewGuid().ToString("N"),
            "panduan.pdf",
            "%PDF-1.7\nstale");
        using var staleResponse = await admin.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
    }

    private HttpClient Client(string userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", role);
        return client;
    }

    private static HttpRequestMessage UploadRequest(
        string eTag,
        string idempotencyKey,
        string fileName,
        string content)
    {
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var body = new MultipartFormDataContent();
        body.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/settings/user-guide")
        {
            Content = body
        };
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
