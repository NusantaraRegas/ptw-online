using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Application;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class PrintPackageApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task IssuedPermitRendersAnOfficialPackageThatCanThenBeDownloaded()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var issued = await IssueAsync(sponsorId, sponsor);

        // Before the worker runs, the package exists but is not an official document yet.
        var queued = await ListAsync(sponsor, issued.Id);
        var pending = Assert.Single(queued.Items);
        Assert.Equal("PENDING", pending.RenderStatus);
        Assert.False(pending.Downloadable);

        using var tooEarly = await sponsor.GetAsync(
            $"/api/v1/permits/{issued.Id}/print-packages/{pending.Id}/content");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);
        Assert.Equal("print_package.not_ready", await ProblemCodeAsync(tooEarly));

        await RenderPendingAsync();

        var rendered = await ListAsync(sponsor, issued.Id);
        var ready = Assert.Single(rendered.Items);
        Assert.Equal("READY", ready.RenderStatus);
        Assert.True(ready.Downloadable);
        Assert.NotNull(ready.Sha256);

        using var download = await sponsor.GetAsync(
            $"/api/v1/permits/{issued.Id}/print-packages/{ready.Id}/content");
        download.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        // The closure guard reads the snapshot, so it must advance with the generated document.
        var snapshot = await db.PrintPackageSnapshots.AsNoTracking().SingleAsync(x => x.PermitId == issued.Id);
        Assert.Equal("READY", snapshot.RenderStatus);
        var document = await db.GeneratedDocuments.AsNoTracking()
            .SingleAsync(x => x.PrintPackageSnapshotId == snapshot.Id);
        Assert.Equal("ptw-form-renderer/2.4.0", document.RendererVersion);
        // Sensitive downloads are material audit events under BR-AUD-001.
        Assert.True(await db.AuditEvents.AsNoTracking().AnyAsync(
            x => x.PermitId == issued.Id && x.EventType == "print_package_downloaded"));
    }

    [Fact]
    public async Task RenderFailureLeavesThePermitIssuedAndAnAdministratorCanRequeueIt()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var issued = await IssueAsync(sponsorId, sponsor);
        var queued = Assert.Single((await ListAsync(sponsor, issued.Id)).Items);

        // Corrupt the immutable snapshot so the render throws, mimicking a renderer fault.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var snapshot = await db.PrintPackageSnapshots.SingleAsync(x => x.Id == queued.Id);
            snapshot.SnapshotJson = "{ not valid json";
            await db.SaveChangesAsync();
        }

        await RenderPendingAsync();

        var afterFailure = Assert.Single((await ListAsync(sponsor, issued.Id)).Items);
        Assert.Equal("RETRYING", afterFailure.RenderStatus);
        Assert.False(afterFailure.Downloadable);
        Assert.NotNull(afterFailure.LastError);

        // The issuance decision is untouched: a render fault must never unwind an approval.
        using var permitResponse = await sponsor.GetAsync($"/api/v1/permits/{issued.Id}");
        permitResponse.EnsureSuccessStatusCode();
        var permit = Required(await permitResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("ISSUED", permit.Status);

        using var sponsorRetry = await sponsor.PostAsync(
            $"/api/v1/permits/{issued.Id}/print-packages/{queued.Id}/retry",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, sponsorRetry.StatusCode);

        using var admin = Client(Unique("admin"), "Administrator", "*");
        using var first = await admin.PostAsync(
            $"/api/v1/permits/{issued.Id}/print-packages/{queued.Id}/retry",
            null);
        first.EnsureSuccessStatusCode();
        using var second = await admin.PostAsync(
            $"/api/v1/permits/{issued.Id}/print-packages/{queued.Id}/retry",
            null);
        second.EnsureSuccessStatusCode();
        var requeued = Required(await second.Content.ReadFromJsonAsync<PrintPackageResponse>());
        Assert.Equal("PENDING", requeued.RenderStatus);
        // Retry is idempotent: repeating it must not stack attempts.
        Assert.Equal(0, requeued.Attempts);
    }

    [Fact]
    public async Task AnotherSponsorCannotReachThePackagesOfAPermitTheyDoNotOwn()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var issued = await IssueAsync(sponsorId, sponsor);
        var queued = Assert.Single((await ListAsync(sponsor, issued.Id)).Items);

        using var intruder = Client(Unique("other-sponsor"), "Sponsor", "ORF");
        using var list = await intruder.GetAsync($"/api/v1/permits/{issued.Id}/print-packages");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var content = await intruder.GetAsync(
            $"/api/v1/permits/{issued.Id}/print-packages/{queued.Id}/content");
        Assert.Equal(HttpStatusCode.Forbidden, content.StatusCode);
    }

    [Fact]
    public async Task DraftPreviewIsWatermarkedAndIsNotPersistedAsAnOfficialPackage()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");

        using var preview = await sponsor.GetAsync($"/api/v1/permits/{draft.Id}/print-packages/preview");
        preview.EnsureSuccessStatusCode();
        var bytes = await preview.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);
        Assert.Contains(
            "PRATINJAU",
            preview.Content.Headers.ContentDisposition?.FileNameStar
                ?? preview.Content.Headers.ContentDisposition?.FileName
                ?? string.Empty,
            StringComparison.Ordinal);

        // A preview never becomes a package, so it can never be offered as closure evidence.
        var packages = await ListAsync(sponsor, draft.Id);
        Assert.Empty(packages.Items);
    }

    /// <summary>Drains the render queue the same way the Worker does.</summary>
    private async Task RenderPendingAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<PrintPackageRenderExecutor>();
        while (await executor.RenderNextAsync(CancellationToken.None))
        {
            // Keep draining until nothing is claimable.
        }
    }

    private static async Task<PagedResponse<PrintPackageResponse>> ListAsync(
        HttpClient client,
        Guid permitId)
    {
        using var response = await client.GetAsync($"/api/v1/permits/{permitId}/print-packages");
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PagedResponse<PrintPackageResponse>>());
    }

    private async Task<PermitResponse> IssueAsync(string sponsorId, HttpClient sponsor)
    {
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");

        var draft = await CreateAsync(sponsor, sponsorId, "ORF");
        draft = await UploadMandatoryDocumentsAsync(sponsor, draft);
        using var submitResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            new SubmitPermitRequest(true, true, true, [])));
        submitResponse.EnsureSuccessStatusCode();
        var submitted = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());

        var validationTask = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");
        using var validateResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "JSA dan requirement telah diverifikasi.",
                ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"])));
        validateResponse.EnsureSuccessStatusCode();
        var validated = Required(await validateResponse.Content.ReadFromJsonAsync<PermitResponse>());

        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            new ApproveAndIssuePermitRequest("Saya menyetujui dan menerbitkan PTW ini.", null)));
        issueResponse.EnsureSuccessStatusCode();
        return Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private static async Task<PermitResponse> UploadMandatoryDocumentsAsync(
        HttpClient client,
        PermitResponse permit)
    {
        permit = await UploadJsaAsync(client, permit);
        foreach (var code in new[] { "ID", "BPJS_TK", "FTW", "ESIMI" })
        {
            permit = await UploadSupportingDocumentAsync(client, permit, code);
        }

        return permit;
    }

    private static async Task<PermitResponse> UploadJsaAsync(HttpClient client, PermitResponse permit)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "jsa.pdf");
        content.Add(new StringContent("JSA"), "category");
        content.Add(new StringContent("JSA"), "supportingDocumentCode");
        content.Add(new StringContent(Required(permit.Draft.JsaDocumentNumber)), "documentNumber");
        content.Add(new StringContent(Required(permit.Draft.JsaRevision)), "documentRevision");
        content.Add(new StringContent(permit.Draft.JsaDate!.Value.ToString("O")), "documentDate");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var mutation = Required(
            await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        return permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
    }

    private static async Task<PermitResponse> UploadSupportingDocumentAsync(
        HttpClient client,
        PermitResponse permit,
        string documentCode)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", $"{documentCode.ToLowerInvariant()}.pdf");
        content.Add(new StringContent("SUPPORTING"), "category");
        content.Add(new StringContent(documentCode), "supportingDocumentCode");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var mutation = Required(
            await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        return permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
    }

    private static async Task<PermitResponse> CreateAsync(HttpClient client, string sponsorId, string location)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/permits", Draft(sponsorId, location));
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private async Task<PermitTaskRecord> PendingTaskAsync(Guid permitId, string type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        return await db.PermitTasks.AsNoTracking().SingleAsync(
            x => x.PermitId == permitId && x.Type == type && x.Status == "PENDING");
    }

    private HttpClient Client(string userId, string roles, string locations)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        client.DefaultRequestHeaders.Add("X-Dev-Locations", locations);
        return client;
    }

    private static HttpRequestMessage Command<T>(HttpMethod method, string path, string etag, T body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private static PermitDraftRequest Draft(string sponsorId, string location)
    {
        var now = DateTimeOffset.UtcNow;
        return new PermitDraftRequest(
            $"Pekerjaan {Guid.NewGuid():N}",
            "Pekerjaan sesuai JSA.",
            location,
            sponsorId,
            "Pelaksana Uji",
            "PT Mitra Uji",
            "HotWork",
            "High",
            now.AddHours(1),
            now.AddDays(1),
            "esimi-test",
            $"ESM-{Guid.NewGuid():N}",
            [],
            [],
            ["JSA"],
            JsaDocumentNumber: "JSA-TEST-001",
            JsaRevision: "1",
            JsaDate: now,
            WorkTypeCodes: ["HOT_WELDING"],
            HeaderClassificationCodes: ["HOT_OPEN_FLAME"]);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    private static T Required<T>(T? value) where T : class => Assert.IsType<T>(value);
}
