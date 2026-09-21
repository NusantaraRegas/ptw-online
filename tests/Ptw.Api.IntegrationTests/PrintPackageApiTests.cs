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
    private static readonly byte[] SignaturePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAAA8CAYAAADha7EVAAACxElEQVR4nO2cQY6DMAxFaTQXyL1m07P0HD1LN3OvHKGjLpAQgpIQ2z82/0mommmxifPjxIH29n6/J0JQJJhnQihAgoYCJFAoQAKFAiRQKEAChQIkUChAAoUCJFAoQAKFAiRQ3Aow35/vb38TH/xMjqHo/JMiCY+C9IdLAZI4uBMgs1wsUrTiIJJAc6C2mAhwDtjndT407C8pr8dtCkhexHL9v0iYTMFagYsuvpnlYI4mQjEBagemxX60ToqMuyJkK/tFyoS5YvBEGmDJQ9AiBfxMO0ugAWaSAT8B0wxajW0N0WoWBLlRfFEGZYqy9tPOEusKv+Xaam1fZZmhmgG/BU2iwxAd8e26kRV+DpAFU4Tsp3W+lI1W+2VHfBGzYLLeFO7p0CP70h1U28azbcoOBoibKVh61KID2zrAWq/3bMVbgmXB5EUcloE/El9vZdq73VIU1tauM2CrOI4CJhlQicy01b6zItTa68tORZgsGtob3Jbze3y1TrtSWbmcsDPSVNwj/u5H8i0W/hacLao+n9l6eKC2WCmC8dvzO3L/NGVAxNQoEVBE1rZ4LrIYiG19SPuoFmDv6LXcWO1dk7aef7QetLzHmzvvuWuKTXQKtliDWPiQFP2WLW3xlR2/yP1YcQFqjYblmsXijkPN9kVPcGvFYDF1loWPUcS2xa3mR8p7p6gaW5I+jnxtvSflc8+2tI81Xu+zH2ZAi7XACF9cGmlbw4oR2nyYAS0yEyITafg78o3cIikDiK25Cva6u966j6fpe/2qSVn4Wh/ToKQrVr5WvizFNzOy2JoEqJn9RgjSCNdAGqpg1LN2Gr4oPmdTcIQOQ0yHJPD3glug+MYFJkBElUicrgEJ0eISUzC50G9E5/vzT9omGZPyevz22mAGJFC4BiRQmAEJFAqQQKEACRQKkEChAAkUCpBAoQAJFAqQTEj+AcBfDu2Urk5nAAAAAElFTkSuQmCC");

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
        var payload = PrintPackageService.ParseSnapshot(snapshot.SnapshotJson);
        Assert.NotNull(payload.Sponsor);
        Assert.Equal("Sponsor Paket Cetak", payload.Sponsor.ActorName);
        Assert.Equal("Officer Permit to Work", payload.Sponsor.ActorPosition);
        Assert.Equal("Operasi", payload.Sponsor.Department);
        Assert.NotNull(payload.Sponsor.Signature);
        Assert.True(payload.Sponsor.SubmittedAt < payload.CreatedAt);
        var document = await db.GeneratedDocuments.AsNoTracking()
            .SingleAsync(x => x.PrintPackageSnapshotId == snapshot.Id);
        Assert.Equal("ptw-form-renderer/3.4.0", document.RendererVersion);
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
        await CreateSponsorProfileAsync(sponsorId);
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var seniorOfficer = Client(Unique("senior-officer"), "AreaOwnerSeniorOfficer", "ORF");
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

        var areaTask = await PendingTaskAsync(validated.Id, "AREA_OPERATION_REVIEW");
        using var areaResponse = await seniorOfficer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            new ReviewAreaOperationsRequest(
                "Kondisi operasi telah diperiksa.",
                ["OPS_ISOLATION", "OPS_ISOLATION_CLOSED_LOCK_VALVES", "OPS_DEPRESSURIZED"],
                null,
                true)));
        areaResponse.EnsureSuccessStatusCode();
        var reviewed = Required(await areaResponse.Content.ReadFromJsonAsync<PermitResponse>());

        var approvalTask = await PendingTaskAsync(reviewed.Id, "AREA_APPROVE_AND_ISSUE");
        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            reviewed.ETag,
            new ApproveAndIssuePermitRequest("Saya menyetujui dan menerbitkan PTW ini.", null)));
        issueResponse.EnsureSuccessStatusCode();
        return Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private async Task CreateSponsorProfileAsync(string sponsorId)
    {
        using var admin = Client(Unique("admin"), "Administrator", "*");
        using var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(
                sponsorId,
                $"user-{Guid.NewGuid():N}",
                "Sponsor Paket Cetak",
                "Officer Permit to Work",
                "Operasi",
                "Development12345"));
        createResponse.EnsureSuccessStatusCode();
        var user = Required(await createResponse.Content.ReadFromJsonAsync<UserAccountResponse>());

        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(SignaturePng);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "signature.png");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/users/{sponsorId}/signature")
        {
            Content = form
        };
        request.Headers.TryAddWithoutValidation("If-Match", user.ETag);
        using var response = await admin.SendAsync(request);
        response.EnsureSuccessStatusCode();
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
