using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class PermitApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task DraftRoundTripsOptionalPlanningReferenceFields()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var request = Draft(sponsorId, "ORF") with
        {
            EquipmentName = " Gas inlet separator ",
            WorkOrderNumber = " WO-2026-001 ",
            AdditionalHazardReference = " Akses sisi utara licin saat hujan "
        };

        using var response = await sponsor.PostAsJsonAsync("/api/v1/permits", request);

        response.EnsureSuccessStatusCode();
        var permit = Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("Gas inlet separator", permit.Draft.EquipmentName);
        Assert.Equal("WO-2026-001", permit.Draft.WorkOrderNumber);
        Assert.Equal("Akses sisi utara licin saat hujan", permit.Draft.AdditionalHazardReference);
    }

    [Fact]
    public async Task SupportingDocumentReferenceDataMatchesTemplateAndOnlyRequiresJsa()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reference-data/supporting-documents");
        response.EnsureSuccessStatusCode();
        var options = Required(
            await response.Content.ReadFromJsonAsync<PermitSupportingDocumentOptionResponse[]>());

        Assert.Equal(15, options.Length);
        var required = Assert.Single(options, option => option.Required);
        Assert.Equal("JSA", required.Code);
        Assert.True(required.RequiresMetadata);
        Assert.Contains(options, option => option.Code == "MSDS" && !option.Required);
    }

    [Fact]
    public async Task SponsorCannotAssignHseOwnedSafetyEquipmentInDraft()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var request = Draft(sponsorId, "ORF") with
        {
            SafetyEquipmentCodes = ["SAFETY_FIRE_EXTINGUISHER"]
        };

        using var response = await sponsor.PostAsJsonAsync("/api/v1/permits", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("permit.safety_equipment.hse_owned", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task WorkTypeReferenceDataMatchesControlledTemplateAndKeepsDuplicateRowsDistinct()
    {
        using var client = Client(Unique("sponsor"), "Sponsor", "ORF");

        var catalog = Required(await client.GetFromJsonAsync<PermitWorkTypeCatalogResponse[]>(
            "/api/v1/reference-data/work-types"));

        var hotWork = Assert.Single(catalog, item => item.PermitClass == "HotWork");
        Assert.Contains(hotWork.Options, option => option.Code == "HOT_WELDING" && option.Label == "Mengelas");
        var sandBlasting = hotWork.Options.Where(option => option.Label == "Sand Blasting").ToArray();
        Assert.Equal(2, sandBlasting.Length);
        Assert.Equal(2, sandBlasting.Select(option => option.Code).Distinct().Count());
        Assert.True(Assert.Single(hotWork.Options, option => option.Code == "HOT_OTHER").RequiresDetail);
        Assert.False(Assert.Single(hotWork.Options, option => option.Code == "HOT_WELDING").RequiresDetail);
    }

    [Fact]
    public async Task DraftRejectsOtherWorkTypeWithoutItsRequiredDescription()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var request = Draft(sponsorId, "ORF") with { WorkTypeCodes = ["HOT_OTHER"] };

        using var response = await sponsor.PostAsJsonAsync("/api/v1/permits", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("permit.work_type_other_detail_required", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task SubmitRejectsJsaSelectionWithoutMatchingUploadedEvidence()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.supporting_document.evidence_required", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task SubmitAlsoRequiresEvidenceForEachOptionalBagian4Selection()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(sponsorId, "ORF") with { RequiredDocumentCodes = ["JSA", "MSDS"] });
        createResponse.EnsureSuccessStatusCode();
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        draft = await UploadJsaAsync(sponsor, draft);

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.supporting_document.evidence_required", await ProblemCodeAsync(response));
        Assert.Contains("MSDS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmissionRejectsLocationOutsideTheReleasedRoutes()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "*");
        var draft = await CreateAsync(sponsor, sponsorId, "FSRU");

        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            new SubmitPermitRequest(true, true, true, [])));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("permit.location.not_released", await ProblemCodeAsync(response));
    }

    [Theory]
    [InlineData("ORF", "SITE_OFFICE")]
    [InlineData("SITE_OFFICE", "ORF")]
    [InlineData("WATER_BASED", "ORF")]
    public async Task ReleasedLocationRoutesApprovalToManagerWithMatchingScope(
        string location,
        string wrongManagerScope)
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", location);
        using var validator = Client(Unique("hse"), "HSEValidator", "*");
        using var wrongManager = Client(Unique("wrong-manager"), "AreaOwnerManager", wrongManagerScope);
        using var correctManager = Client(Unique("correct-manager"), "AreaOwnerManager", location);
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId, location);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        var request = new ApproveAndIssuePermitRequest("Disetujui oleh pemilik wilayah.", null);

        using var wrongResponse = await wrongManager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request));
        Assert.Equal(HttpStatusCode.Forbidden, wrongResponse.StatusCode);

        using var correctResponse = await correctManager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request));
        correctResponse.EnsureSuccessStatusCode();
        var issued = Required(await correctResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("ISSUED", issued.Status);
    }

    [Fact]
    public async Task SubmitCreatesExactlyOneHseTaskAndNoGasValidatorTask()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);

        Assert.Equal("UNDER_VALIDATION", submitted.Status);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var tasks = await db.PermitTasks.AsNoTracking()
            .Where(x => x.PermitId == submitted.Id && x.Status == "PENDING")
            .ToListAsync();
        var task = Assert.Single(tasks);
        Assert.Equal("HSE_VALIDATION", task.Type);
        Assert.Equal("HSEValidator", task.RequiredRole);
        Assert.DoesNotContain(tasks, x => x.Type.Contains("GAS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SponsorCannotSelfValidateButAnotherHseValidatorCan()
    {
        var sponsorId = Unique("hse-sponsor");
        using var sponsor = Client(sponsorId, "Sponsor,HSEValidator", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var validationTask = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");

        using var selfResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "Saya memvalidasi sendiri.",
                ["SAFETY_FIRE_EXTINGUISHER"])));
        Assert.Equal(HttpStatusCode.Conflict, selfResponse.StatusCode);
        Assert.Equal("permit.validation.self_validation_forbidden", await ProblemCodeAsync(selfResponse));

        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var validateResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{validationTask.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "JSA dan requirement telah diverifikasi.",
                ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"])));
        validateResponse.EnsureSuccessStatusCode();
        var validated = Required(await validateResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_AREA_APPROVAL", validated.Status);
        Assert.Equal("HSE", validated.Workflow.Hse.Code);
        Assert.True(validated.Workflow.Hse.Completed);
        Assert.Equal(
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            validated.Workflow.Hse.SafetyEquipmentCodes);
        Assert.Empty(validated.Draft.SafetyEquipmentCodes ?? []);

        var approvalTask = await PendingTaskAsync(submitted.Id, "AREA_APPROVE_AND_ISSUE");
        Assert.Equal(submitted.Version, approvalTask.PermitVersion);
    }

    [Fact]
    public async Task ApproveAndIssueCommitsDecisionStateAuditOutboxAndPrintSnapshotAtomically()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        var managerId = Unique("manager");
        using var manager = Client(managerId, "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        var request = new ApproveAndIssuePermitRequest(
            "Saya menyetujui dan menerbitkan PTW ini.",
            null);
        var key = Guid.NewGuid().ToString("N");

        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request,
            key));
        Assert.True(issueResponse.IsSuccessStatusCode, await issueResponse.Content.ReadAsStringAsync());
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("ISSUED", issued.Status);
        Assert.Equal("MANAGER", issued.Workflow.Approval.Capacity);
        Assert.NotEqual(Guid.Empty, issued.Workflow.Approval.AuthorizationId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var decision = await db.PermitDecisions.AsNoTracking().SingleAsync(x => x.PermitId == issued.Id);
        var snapshot = await db.PrintPackageSnapshots.AsNoTracking().SingleAsync(x => x.PermitId == issued.Id);
        var document = await db.GeneratedDocuments.AsNoTracking()
            .SingleAsync(x => x.PrintPackageSnapshotId == snapshot.Id);
        Assert.Equal(decision.Id, snapshot.DecisionId);
        Assert.Equal("PENDING", snapshot.RenderStatus);
        Assert.Equal("PENDING", document.RenderStatus);
        Assert.True(await db.AuditEvents.AsNoTracking().AnyAsync(
            x => x.PermitId == issued.Id && x.EventType == "permit_issued"));
        Assert.True(await db.OutboxMessages.AsNoTracking().AnyAsync(
            x => x.AggregateId == issued.Id && x.EventType == "permit_issued"));

        using var replay = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request,
            key));
        replay.EnsureSuccessStatusCode();
        Assert.Equal(1, await db.PermitDecisions.AsNoTracking().CountAsync(x => x.PermitId == issued.Id));

        using var mismatch = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            request with { Statement = "Payload berbeda." },
            key));
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(mismatch));
    }

    [Fact]
    public async Task RevisionCancelsAreaTaskAndResubmitCreatesFreshHseTaskForNewVersion()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var areaTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");

        using var revisionResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/revision",
            validated.ETag,
            new PermitReasonRequest("JSA harus diperbarui.")));
        revisionResponse.EnsureSuccessStatusCode();
        var revision = Required(await revisionResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("REVISION_REQUIRED", revision.Status);
        Assert.False(revision.Workflow.Hse.Completed);

        using var patchResponse = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{revision.Id}/draft",
            revision.ETag,
            revision.Draft with { Title = "Versi material baru" },
            idempotencyKey: null));
        patchResponse.EnsureSuccessStatusCode();
        var updated = Required(await patchResponse.Content.ReadFromJsonAsync<PermitResponse>());
        using var submitResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{updated.Id}/submit",
            updated.ETag,
            ReadyToSubmit()));
        submitResponse.EnsureSuccessStatusCode();
        var resubmitted = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal(updated.Version, resubmitted.Version);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal("CANCELLED", (await db.PermitTasks.SingleAsync(x => x.Id == areaTask.Id)).Status);
        Assert.Single(await db.PermitTasks.Where(x => x.PermitId == updated.Id
            && x.PermitVersion == updated.Version
            && x.Type == "HSE_VALIDATION"
            && x.Status == "PENDING").ToListAsync());
    }

    [Fact]
    public async Task StaleIfMatchReturnsConflictAndIdempotencyPayloadMismatchIsRejected()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var draft = await CreateAsync(sponsor, sponsorId, "ORF");
        using var updateResponse = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            draft.ETag,
            draft.Draft with { Title = "Versi kedua" },
            null));
        updateResponse.EnsureSuccessStatusCode();

        using var stale = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            draft.ETag,
            draft.Draft with { Title = "Versi stale" },
            null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency.conflict", await ProblemCodeAsync(stale));
    }

    [Fact]
    public async Task AttachmentUploadAcceptsVerifiedPdfJpegAndPngSignaturesAsPendingEvidence()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var permit = await CreateAsync(sponsor, sponsorId, "ORF");
        var files = new[]
        {
            ("jsa.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n")),
            ("photo.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }),
            ("scan.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
        };
        PermitAttachmentResponse? firstAttachment = null;

        foreach (var (fileName, mediaType, bytes) in files)
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            content.Add(file, "file", fileName);
            content.Add(new StringContent("SUPPORTING"), "category");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/permits/{permit.Id}/attachments")
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var response = await sponsor.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var mutation = Required(await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
            Assert.Equal("PENDING", mutation.Attachment.ScanStatus);
            Assert.Equal("SUPPORTING", mutation.Attachment.Category);
            Assert.Equal(mediaType, mutation.Attachment.MediaType);
            Assert.Equal(64, mutation.Attachment.Sha256.Length);
            firstAttachment ??= mutation.Attachment;
            permit = permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
        }

        using var pendingDownload = await sponsor.GetAsync(
            $"/api/v1/permits/{permit.Id}/attachments/{Required(firstAttachment).Id}/content");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pendingDownload.StatusCode);
        Assert.Equal("attachment.not_clean", await ProblemCodeAsync(pendingDownload));

        using var otherSponsor = Client(Unique("other-sponsor"), "Sponsor", "ORF");
        using var crossSponsorList = await otherSponsor.GetAsync($"/api/v1/permits/{permit.Id}/attachments");
        Assert.Equal(HttpStatusCode.Forbidden, crossSponsorList.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(crossSponsorList));
    }

    [Fact]
    public async Task AttachmentUploadRejectsExtensionAndSignatureMismatch()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        var permit = await CreateAsync(sponsor, sponsorId, "ORF");
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "forged.pdf");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await sponsor.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("attachment.media_type_invalid", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task ClosureRequestRejectsSignedFieldCopyThatIsNotClean()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var validated = await CreateAndValidateAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(validated.Id, "AREA_APPROVE_AND_ISSUE");
        using var issueResponse = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            validated.ETag,
            new ApproveAndIssuePermitRequest("Disetujui dan diterbitkan.", null)));
        issueResponse.EnsureSuccessStatusCode();
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());

        Guid printPackageId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var package = await db.PrintPackageSnapshots.SingleAsync(x => x.PermitId == issued.Id);
            package.RenderStatus = "READY";
            printPackageId = package.Id;
            await db.SaveChangesAsync();
        }

        using var uploadContent = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadContent.Add(file, "file", "signed-field-copy.pdf");
        uploadContent.Add(new StringContent("SIGNED_FIELD_COPY"), "category");
        uploadContent.Add(new StringContent("SFC-001"), "documentNumber");
        uploadContent.Add(new StringContent("1"), "documentRevision");
        uploadContent.Add(new StringContent(DateTimeOffset.UtcNow.ToString("O")), "documentDate");
        uploadContent.Add(new StringContent(printPackageId.ToString()), "printPackageId");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/attachments")
        {
            Content = uploadContent
        };
        uploadRequest.Headers.TryAddWithoutValidation("If-Match", issued.ETag);
        uploadRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var uploadResponse = await sponsor.SendAsync(uploadRequest);
        Assert.True(uploadResponse.IsSuccessStatusCode, await uploadResponse.Content.ReadAsStringAsync());
        var upload = Required(await uploadResponse.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        Assert.Equal("PENDING", upload.Attachment.ScanStatus);

        using var closureResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests",
            upload.ETag,
            new RequestClosureRequest(
                printPackageId,
                [upload.Attachment.Id],
                "Pekerjaan dan handback pada hardcopy telah selesai.",
                true,
                true)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, closureResponse.StatusCode);
        Assert.Equal("permit.closure.evidence_invalid", await ProblemCodeAsync(closureResponse));
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Empty(await verificationDb.PermitTasks.Where(x =>
            x.PermitId == issued.Id && x.Type == "AREA_CLOSE_VERIFICATION").ToListAsync());
    }

    private async Task<PermitResponse> CreateAndValidateAsync(
        HttpClient sponsor,
        HttpClient validator,
        string sponsorId,
        string location = "ORF")
    {
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId, location);
        var task = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");
        using var response = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest(
                "JSA dan requirement telah diverifikasi.",
                ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"])));
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private static async Task<PermitResponse> CreateAndSubmitAsync(
        HttpClient sponsor,
        string sponsorId,
        string location = "ORF")
    {
        var draft = await CreateAsync(sponsor, sponsorId, location);
        draft = await UploadJsaAsync(sponsor, draft);
        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            ReadyToSubmit()));
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
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

    private static HttpRequestMessage Command<T>(
        HttpMethod method,
        string path,
        string etag,
        T body,
        string? idempotencyKey = "auto")
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(
                "Idempotency-Key",
                idempotencyKey == "auto" ? Guid.NewGuid().ToString("N") : idempotencyKey);
        }

        return request;
    }

    private static SubmitPermitRequest ReadyToSubmit() => new(true, true, true, []);

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
            WorkTypeCodes: ["HOT_WELDING"]);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    private static T Required<T>(T? value) where T : class => Assert.IsType<T>(value);
}
