using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Application;
using Ptw.Contracts;
using Ptw.Domain;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

/// <summary>
/// One PTW walked end to end through the public HTTP surface only: the Contractor fills the
/// initial PTW, the Sponsor approves and thereby submits it, the PIC HSE validates Bagian 5,
/// the SO/Officer reviews Bagian 7, the Manager approves and issues, the Worker renders the
/// official package, the Sponsor downloads it and later returns the signed field copy with
/// Bagian 9, the Manager verifies Bagian 10, and the PTW ends CLOSED. Every stage is observed
/// the way the Angular shell observes it (detail, task bell, Papan Operasi, riwayat) rather
/// than by reading tables, so a regression anywhere along the chain breaks this scenario.
/// </summary>
[Collection(PtwApiTestGroup.Name)]
public sealed class PermitLifecycleEndToEndTests(PtwApiFactory factory)
{
    private const string Location = "ORF";

    [Fact]
    public async Task ContractorDraftTravelsThroughEveryStageUntilTheAreaOwnerClosesIt()
    {
        var actors = await Actors.CreateAsync(this);
        using var contractor = actors.Contractor;
        using var sponsor = actors.Sponsor;
        using var hse = actors.Hse;
        using var officer = actors.Officer;
        using var manager = actors.Manager;
        using var otherAreaManager = Client(Unique("manager-site-office"), "AreaOwnerManager", "SITE_OFFICE");
        using var otherContractor = Client(Unique("contractor-other"), "Requester", "*");
        using var admin = Client(Unique("admin"), "Administrator", "*");

        // Stage 1: the Contractor fills the initial PTW and uploads the mandatory evidence.
        var title = $"Pengelasan flange {Guid.NewGuid():N}";
        using var createResponse = await contractor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(actors.SponsorId, title) with { SubmitterType = "CONTRACTOR" });
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("DRAFT", draft.Status);
        Assert.Null(draft.PermitNumber);
        Assert.Equal(1, draft.Version);
        Assert.Equal("DRAFTING", draft.Workflow.Initiation.Status);
        Assert.Equal(actors.ContractorId, draft.Workflow.Initiation.RequesterId);
        Assert.Equal(Actors.ContractorName, draft.Workflow.Initiation.RequesterName);
        Assert.Equal(Actors.SponsorName, draft.SponsorName);

        draft = await UploadMandatoryDocumentsAsync(contractor, draft);
        Assert.Equal(1, draft.Version);
        var attachments = Required(await contractor.GetFromJsonAsync<List<PermitAttachmentResponse>>(
            $"/api/v1/permits/{draft.Id}/attachments"));
        Assert.Equal(5, attachments.Count);
        Assert.All(attachments, attachment => Assert.Equal("CLEAN", attachment.ScanStatus));
        Assert.Contains(attachments, attachment => attachment.Category == "JSA");

        // A Contractor only ever sees the PTWs it initiated itself.
        using var foreignDetail = await otherContractor.GetAsync($"/api/v1/permits/{draft.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignDetail.StatusCode);

        // Stage 2: hand-over to the Sponsor. The PTW stays DRAFT; only the Sponsor gets a task.
        var awaiting = await CommandAsync<PermitResponse>(
            contractor, $"/api/v1/permits/{draft.Id}/initial-submit", draft.ETag, new { });
        Assert.Equal("DRAFT", awaiting.Status);
        Assert.Equal("AWAITING_SPONSOR_APPROVAL", awaiting.Workflow.Initiation.Status);
        var initialTask = await SingleTaskAsync(sponsor, draft.Id, "SPONSOR_INITIAL_APPROVAL");
        await AssertNoTaskAsync(hse, draft.Id);
        await AssertNoTaskAsync(manager, draft.Id);

        // Stage 3: the Sponsor approves, which is the submission to the PIC HSE.
        var submitted = await CommandAsync<PermitResponse>(
            sponsor,
            $"/api/v1/tasks/{initialTask.Id}/approve-initial",
            awaiting.ETag,
            new InitialApprovalRequest("PTW awal lengkap dan disetujui.", true));
        Assert.Equal("UNDER_VALIDATION", submitted.Status);
        Assert.Equal("APPROVED", submitted.Workflow.Initiation.Status);
        Assert.Equal(1, submitted.Version);
        var wib = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        Assert.Matches($@"^PTW/\d{{4}}/{wib:dd}/{wib:MM}/{wib:yyyy}$", Required(submitted.PermitNumber));
        var validationTask = await SingleTaskAsync(hse, draft.Id, "HSE_VALIDATION");
        Assert.Equal(submitted.PermitNumber, validationTask.PermitNumber);
        await AssertNoTaskAsync(sponsor, draft.Id);
        await AssertNoTaskAsync(officer, draft.Id);
        await AssertNoTaskAsync(manager, draft.Id);

        // Stage 4: the PIC HSE validates and fixes Bagian 5.
        var validated = await CommandAsync<PermitResponse>(
            hse,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "JSA, prosedur kerja, dan requirement telah diverifikasi.",
                ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"]));
        Assert.Equal("AWAITING_AREA_APPROVAL", validated.Status);
        Assert.True(validated.Workflow.Hse.Completed);
        Assert.Equal(actors.HseId, validated.Workflow.Hse.ActorId);
        Assert.Equal(Actors.HseName, validated.Workflow.Hse.ActorName);
        Assert.Equal(["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"], validated.Workflow.Hse.SafetyEquipmentCodes);
        Assert.False(validated.Workflow.AreaOperations.Completed);
        var reviewTask = await SingleTaskAsync(officer, draft.Id, "AREA_OPERATION_REVIEW");
        await AssertNoTaskAsync(hse, draft.Id);
        await AssertNoTaskAsync(manager, draft.Id);

        // Stage 5: the SO/Officer of the area reviews Bagian 7; the status does not move yet.
        var reviewed = await CommandAsync<PermitResponse>(
            officer,
            $"/api/v1/tasks/{reviewTask.Id}/review-area-operations",
            validated.ETag,
            new ReviewAreaOperationsRequest(
                "Isolasi, penutupan valve, dan depressurisasi telah diperiksa di lapangan.",
                [
                    PermitOperationalConditionCatalog.Isolation,
                    PermitOperationalConditionCatalog.IsolationClosedLockValves,
                    PermitOperationalConditionCatalog.Depressurized
                ],
                null,
                true));
        Assert.Equal("AWAITING_AREA_APPROVAL", reviewed.Status);
        Assert.True(reviewed.Workflow.AreaOperations.Completed);
        Assert.Equal(Actors.OfficerName, reviewed.Workflow.AreaOperations.ActorName);
        Assert.Equal(Actors.OfficerPosition, reviewed.Workflow.AreaOperations.ActorPosition);
        Assert.False(reviewed.Workflow.Approval.Completed);
        var approvalTask = await SingleTaskAsync(manager, draft.Id, "AREA_APPROVE_AND_ISSUE");
        await AssertNoTaskAsync(officer, draft.Id);
        await AssertNoTaskAsync(otherAreaManager, draft.Id);

        // Stage 6: the Manager approves and issues; the official package is queued, not ready.
        var issued = await CommandAsync<PermitResponse>(
            manager,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            reviewed.ETag,
            new ApproveAndIssuePermitRequest("Saya menyetujui dan menerbitkan PTW ini.", null));
        Assert.Equal("ISSUED", issued.Status);
        Assert.Equal(1, issued.Version);
        Assert.Equal(submitted.PermitNumber, issued.PermitNumber);
        Assert.True(issued.Workflow.Approval.Completed);
        Assert.Equal("MANAGER", issued.Workflow.Approval.Capacity);
        Assert.Equal(Actors.ManagerName, issued.Workflow.Approval.ActorName);
        Assert.Equal(Actors.ManagerPosition, issued.Workflow.Approval.ActorPosition);
        await AssertNoTaskAsync(manager, draft.Id);

        var board = Required(await manager.GetFromJsonAsync<OperationsBoardResponse>(
            $"/api/v1/operations?search={Uri.EscapeDataString(Required(issued.PermitNumber))}"));
        var boardItem = Assert.Single(board.Items, item => item.Id == draft.Id);
        Assert.Equal("ISSUED", boardItem.Status);

        var queued = Assert.Single((await ListPackagesAsync(sponsor, draft.Id)).Items);
        Assert.Equal("PENDING", queued.RenderStatus);
        Assert.False(queued.Downloadable);
        using var tooEarly = await sponsor.GetAsync(
            $"/api/v1/permits/{draft.Id}/print-packages/{queued.Id}/content");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);

        // Stage 7: the Worker renders the package; the Sponsor downloads the official document.
        await RenderPendingAsync();
        var ready = Assert.Single((await ListPackagesAsync(sponsor, draft.Id)).Items);
        Assert.Equal(queued.Id, ready.Id);
        Assert.Equal("READY", ready.RenderStatus);
        Assert.True(ready.Downloadable);
        using var download = await sponsor.GetAsync(
            $"/api/v1/permits/{draft.Id}/print-packages/{ready.Id}/content");
        Assert.True(download.IsSuccessStatusCode, await download.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        var pdf = await download.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);

        // Stage 8: a suspension stops the work at once and only returns the PTW to ISSUED.
        var suspended = await CommandAsync<PermitResponse>(
            hse,
            $"/api/v1/permits/{draft.Id}/suspensions",
            issued.ETag,
            new PermitReasonRequest("Alarm gas di area kerja; pekerjaan dihentikan."));
        Assert.Equal("SUSPENDED", suspended.Status);
        Assert.True(suspended.Workflow.Suspension.Suspended);
        Assert.Equal(actors.HseId, suspended.Workflow.Suspension.SuspendedBy);
        var resumed = await CommandAsync<PermitResponse>(
            manager,
            $"/api/v1/permits/{draft.Id}/suspensions/resolve",
            suspended.ETag,
            new ResolveSuspensionRequest("Area telah dinyatakan aman kembali oleh HSE."));
        Assert.Equal("ISSUED", resumed.Status);
        Assert.Null(resumed.SuspensionReason);
        Assert.Equal(actors.ManagerId, resumed.Workflow.Suspension.ResolvedBy);

        // Stage 9: the Sponsor returns the signed field copy of that exact package with Bagian 9.
        var signedCopy = await UploadSignedFieldCopyAsync(sponsor, draft.Id, resumed.ETag, ready.Id);
        Assert.Equal("CLEAN", signedCopy.Attachment.ScanStatus);
        Assert.Equal(ready.Id, signedCopy.Attachment.PrintPackageId);
        var closureRequested = await CommandAsync<PermitResponse>(
            sponsor,
            $"/api/v1/permits/{draft.Id}/closure-requests",
            signedCopy.ETag,
            new RequestClosureRequest(
                ready.Id,
                [signedCopy.Attachment.Id],
                "Pekerjaan selesai dan handback telah ditandatangani pada hardcopy.",
                true,
                true,
                new PermitWorkCompletionRequest(true, true, null, "Pelaksana Uji", "B-001", "S-001")));
        Assert.Equal("CLOSURE_REQUESTED", closureRequested.Status);
        Assert.True(closureRequested.Workflow.Closure.Requested);
        Assert.Equal(ready.Id, closureRequested.Workflow.Closure.PrintPackageId);
        Assert.Equal([signedCopy.Attachment.Id], closureRequested.Workflow.Closure.SignedFieldCopyAttachmentIds);
        Assert.True(Required(closureRequested.Workflow.Closure.Completion).WorkCompleted);
        var closureTask = await SingleTaskAsync(manager, draft.Id, "AREA_CLOSE_VERIFICATION");
        Assert.Equal(closureTask.Id, (await SingleTaskAsync(officer, draft.Id, "AREA_CLOSE_VERIFICATION")).Id);
        await AssertNoTaskAsync(hse, draft.Id);
        await AssertNoTaskAsync(otherAreaManager, draft.Id);

        // Stage 10: the area owner verifies Bagian 10 and the PTW reaches its terminal state.
        var closed = await CommandAsync<PermitResponse>(
            manager,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            closureRequested.ETag,
            new ClosePermitRequest(
                "Bagian 10 dan hardcopy telah diverifikasi.",
                Actors.OfficerName,
                true,
                true,
                true,
                true,
                true,
                true));
        Assert.Equal("CLOSED", closed.Status);
        Assert.True(closed.Workflow.Closure.Closed);
        Assert.Equal(actors.ManagerId, closed.Workflow.Closure.ClosedBy);
        Assert.Equal(Actors.OfficerName, closed.Workflow.Closure.OfficerName);
        Assert.Equal(1, closed.Version);
        foreach (var client in new[] { contractor, sponsor, hse, officer, manager })
        {
            await AssertNoTaskAsync(client, draft.Id);
        }

        using var suspendAfterClose = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/suspensions",
            closed.ETag,
            new PermitReasonRequest("Terlambat.")));
        Assert.Equal(HttpStatusCode.Conflict, suspendAfterClose.StatusCode);
        Assert.Equal("permit.invalid_transition", await ProblemCodeAsync(suspendAfterClose));
        using var closeAgain = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/closure-requests",
            closed.ETag,
            new RequestClosureRequest(
                ready.Id,
                [signedCopy.Attachment.Id],
                "Pengajuan ulang setelah ditutup.",
                true,
                true,
                new PermitWorkCompletionRequest(true, true, null, "Pelaksana Uji", "B-001", "S-001"))));
        Assert.Equal("permit.invalid_transition", await ProblemCodeAsync(closeAgain));
        Assert.Equal(HttpStatusCode.Conflict, closeAgain.StatusCode);

        var adminBoard = Required(await admin.GetFromJsonAsync<OperationsBoardResponse>(
            $"/api/v1/operations?status=CLOSED&search={Uri.EscapeDataString(Required(closed.PermitNumber))}"));
        Assert.Equal("CLOSED", Assert.Single(adminBoard.Items, item => item.Id == draft.Id).Status);

        // The riwayat shown to the Sponsor tells the whole story in order.
        var activity = Required(await sponsor.GetFromJsonAsync<PagedResponse<PermitActivityResponse>>(
            $"/api/v1/permits/{draft.Id}/activity?limit=100"));
        var eventTypes = activity.Items.OrderBy(entry => entry.Sequence).Select(entry => entry.EventType).ToList();
        AssertInOrder(
            eventTypes,
            "permit_draft_created",
            "permit_initial_submitted",
            "permit_initial_approved",
            "hse_validation_completed",
            "area_operations_review_completed",
            "permit_issued",
            "permit_suspended",
            "permit_suspension_resolved",
            "signed_field_copy_uploaded",
            "closure_requested",
            "permit_closed");
        Assert.Equal(5, eventTypes.Count(type => type == "permit_attachment_added"));
        var versions = Required(await sponsor.GetFromJsonAsync<PagedResponse<PermitVersionResponse>>(
            $"/api/v1/permits/{draft.Id}/versions"));
        var onlyRevision = Assert.Single(versions.Items);
        Assert.Equal(1, onlyRevision.Version);
        Assert.Equal(title, onlyRevision.Snapshot.Title);

        // Persistence agrees with what the API reported: audit, outbox, and no dangling task.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var auditTypes = await db.AuditEvents.AsNoTracking()
            .Where(x => x.PermitId == draft.Id)
            .Select(x => x.EventType)
            .ToListAsync();
        foreach (var type in new[] { "permit_issued", "permit_closed", "print_package_downloaded" })
        {
            Assert.Contains(type, auditTypes);
        }
        foreach (var type in new[] { "permit_submitted", "permit_issued", "permit_closed" })
        {
            Assert.True(
                await db.OutboxMessages.AsNoTracking().AnyAsync(x => x.AggregateId == draft.Id && x.EventType == type),
                $"outbox message {type} missing");
        }
        Assert.Empty(await db.PermitTasks.AsNoTracking()
            .Where(x => x.PermitId == draft.Id && x.Status == "PENDING")
            .ToListAsync());
        var snapshot = await db.PrintPackageSnapshots.AsNoTracking().SingleAsync(x => x.PermitId == draft.Id);
        Assert.Equal("READY", snapshot.RenderStatus);
    }

    /// <summary>The five personas of the baseline flow, each with a server-side profile.</summary>
    private sealed class Actors
    {
        public const string ContractorName = "Kontraktor Mitra Uji";
        public const string SponsorName = "Sponsor Lifecycle";
        public const string HseName = "PIC HSE Lifecycle";
        public const string OfficerName = "Officer Operasi Lifecycle";
        public const string OfficerPosition = "Officer II Gas Delivery Operation";
        public const string ManagerName = "Manager Lifecycle";
        public const string ManagerPosition = "Manager Gas Distribution&ORF Management";

        public required string ContractorId { get; init; }
        public required string SponsorId { get; init; }
        public required string HseId { get; init; }
        public required string ManagerId { get; init; }
        public required HttpClient Contractor { get; init; }
        public required HttpClient Sponsor { get; init; }
        public required HttpClient Hse { get; init; }
        public required HttpClient Officer { get; init; }
        public required HttpClient Manager { get; init; }

        public static async Task<Actors> CreateAsync(PermitLifecycleEndToEndTests test)
        {
            var contractorId = Unique("lifecycle-contractor");
            var sponsorId = Unique("lifecycle-sponsor");
            var hseId = Unique("lifecycle-hse");
            var officerId = Unique("lifecycle-officer");
            var managerId = Unique("lifecycle-manager");
            await test.CreateProfileAsync(contractorId, ContractorName, "Supervisor Mekanikal", "PT Mitra Uji", withSignature: true);
            await test.CreateProfileAsync(sponsorId, SponsorName, "Officer Permit to Work", "Operasi", withSignature: true);
            await test.CreateProfileAsync(hseId, HseName, "Sr. Officer II Health & Safety", "HSSE", withSignature: false);
            await test.CreateProfileAsync(officerId, OfficerName, OfficerPosition, "Gas Distribution&ORF Management", withSignature: false);
            await test.CreateProfileAsync(managerId, ManagerName, ManagerPosition, "Gas Distribution&ORF Management", withSignature: false);
            return new Actors
            {
                ContractorId = contractorId,
                SponsorId = sponsorId,
                HseId = hseId,
                ManagerId = managerId,
                Contractor = test.Client(contractorId, "Requester", "*", ContractorName),
                Sponsor = test.Client(sponsorId, "Sponsor", Location, SponsorName),
                Hse = test.Client(hseId, "HSEValidator", Location, HseName),
                Officer = test.Client(officerId, "AreaOwnerSeniorOfficer", Location, OfficerName),
                Manager = test.Client(managerId, "AreaOwnerManager", Location, ManagerName)
            };
        }
    }

    private async Task CreateProfileAsync(
        string subjectId,
        string displayName,
        string position,
        string department,
        bool withSignature)
    {
        using var admin = Client(Unique("admin"), "Administrator", "*");
        using var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new CreateUserRequest(subjectId, $"user-{Guid.NewGuid():N}", displayName, position, department, "Development12345"));
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
        if (!withSignature)
        {
            return;
        }

        var user = Required(await createResponse.Content.ReadFromJsonAsync<UserAccountResponse>());
        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(TestAssets.SignaturePng);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "signature.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{subjectId}/signature")
        {
            Content = form
        };
        request.Headers.TryAddWithoutValidation("If-Match", user.ETag);
        using var response = await admin.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
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

    private static async Task<PermitTaskResponse> SingleTaskAsync(HttpClient client, Guid permitId, string type)
    {
        var tasks = Required(await client.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        var task = Assert.Single(tasks.Items, item => item.PermitId == permitId);
        Assert.Equal(type, task.Type);
        Assert.Equal("PENDING", task.Status);
        return task;
    }

    private static async Task AssertNoTaskAsync(HttpClient client, Guid permitId)
    {
        var tasks = Required(await client.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        Assert.DoesNotContain(tasks.Items, task => task.PermitId == permitId);
    }

    private static async Task<PagedResponse<PrintPackageResponse>> ListPackagesAsync(HttpClient client, Guid permitId)
    {
        using var response = await client.GetAsync($"/api/v1/permits/{permitId}/print-packages");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Required(await response.Content.ReadFromJsonAsync<PagedResponse<PrintPackageResponse>>());
    }

    private static async Task<T> CommandAsync<T>(HttpClient client, string path, string etag, object body)
        where T : class
    {
        using var response = await client.SendAsync(Command(HttpMethod.Post, path, etag, body));
        Assert.True(response.IsSuccessStatusCode, $"{path}: {await response.Content.ReadAsStringAsync()}");
        return Required(await response.Content.ReadFromJsonAsync<T>());
    }

    private static async Task<PermitResponse> UploadMandatoryDocumentsAsync(HttpClient client, PermitResponse permit)
    {
        permit = await UploadAsync(
            client,
            permit,
            "jsa.pdf",
            [
                ("category", "JSA"),
                ("supportingDocumentCode", "JSA"),
                ("documentNumber", Required(permit.Draft.JsaDocumentNumber)),
                ("documentRevision", Required(permit.Draft.JsaRevision)),
                ("documentDate", permit.Draft.JsaDate!.Value.ToString("O"))
            ]);
        foreach (var code in new[] { "WORK_PROCEDURE", "ID", "BPJS_TK", "FTW" })
        {
            permit = await UploadAsync(
                client,
                permit,
                $"{code.ToLowerInvariant()}.pdf",
                [("category", "SUPPORTING"), ("supportingDocumentCode", code)]);
        }

        return permit;
    }

    private static async Task<PermitAttachmentMutationResponse> UploadSignedFieldCopyAsync(
        HttpClient client,
        Guid permitId,
        string etag,
        Guid printPackageId)
    {
        using var response = await SendUploadAsync(
            client,
            permitId,
            etag,
            "signed-field-copy.pdf",
            [
                ("category", "SIGNED_FIELD_COPY"),
                ("documentNumber", "SFC-001"),
                ("documentRevision", "1"),
                ("documentDate", DateTimeOffset.UtcNow.ToString("O")),
                ("printPackageId", printPackageId.ToString())
            ]);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Required(await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
    }

    private static async Task<PermitResponse> UploadAsync(
        HttpClient client,
        PermitResponse permit,
        string fileName,
        IReadOnlyList<(string Name, string Value)> fields)
    {
        using var response = await SendUploadAsync(client, permit.Id, permit.ETag, fileName, fields);
        Assert.True(response.IsSuccessStatusCode, $"{fileName}: {await response.Content.ReadAsStringAsync()}");
        var mutation = Required(await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        return permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
    }

    private static async Task<HttpResponseMessage> SendUploadAsync(
        HttpClient client,
        Guid permitId,
        string etag,
        string fileName,
        IReadOnlyList<(string Name, string Value)> fields)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestAssets.MinimalPdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", fileName);
        foreach (var (name, value) in fields)
        {
            content.Add(new StringContent(value), name);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permits/{permitId}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private HttpClient Client(string userId, string roles, string locations, string? displayName = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", displayName ?? userId);
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

    private static PermitDraftRequest Draft(string sponsorId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new PermitDraftRequest(
            title,
            "Pengelasan flange sesuai JSA.",
            Location,
            sponsorId,
            "Pelaksana Uji",
            "PT Mitra Uji",
            "HotWork",
            "High",
            now.AddHours(1),
            now.AddDays(1),
            [],
            [],
            ["JSA", "WORK_PROCEDURE"],
            JsaDocumentNumber: "JSA-E2E-001",
            JsaRevision: "1",
            JsaDate: now,
            WorkTypeCodes: ["HOT_WELDING"],
            HeaderClassificationCodes: ["HOT_OPEN_FLAME"]);
    }

    /// <summary>Asserts that <paramref name="expected"/> occurs as an ordered subsequence of <paramref name="actual"/>.</summary>
    private static void AssertInOrder(List<string> actual, params string[] expected)
    {
        var position = 0;
        foreach (var type in expected)
        {
            var index = -1;
            for (var i = position; i < actual.Count; i++)
            {
                if (actual[i] == type)
                {
                    index = i;
                    break;
                }
            }

            Assert.True(index >= 0, $"'{type}' not found after position {position} in: {string.Join(", ", actual)}");
            position = index + 1;
        }
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    private static T Required<T>(T? value) where T : class => Assert.IsType<T>(value);
}
