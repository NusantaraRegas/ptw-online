using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Domain;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

/// <summary>
/// Follow-up of the UAT of 12 October 2026 (UAT-2026-10-12): official numbering, separate APD
/// save, revision self checks, Bagian 9 closure, PJS approvals, and the Contractor initial PTW.
/// </summary>
[Collection(PtwApiTestGroup.Name)]
public sealed class UatFollowUpApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task SubmitAllocatesOfficialNumberWithWibDateAndYearlySequence()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");

        var first = await CreateAndSubmitAsync(sponsor, sponsorId);
        var second = await CreateAndSubmitAsync(sponsor, sponsorId);

        var wib = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var pattern = $@"^PTW/(\d{{4}})/{wib:dd}/{wib:MM}/{wib:yyyy}$";
        var firstMatch = System.Text.RegularExpressions.Regex.Match(Required(first.PermitNumber), pattern);
        var secondMatch = System.Text.RegularExpressions.Regex.Match(Required(second.PermitNumber), pattern);
        Assert.True(firstMatch.Success, first.PermitNumber);
        Assert.True(secondMatch.Success, second.PermitNumber);
        Assert.Equal(
            int.Parse(firstMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + 1,
            int.Parse(secondMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var sequence = await db.PermitNumberSequences.AsNoTracking().SingleAsync(x => x.Year == wib.Year);
        Assert.True(sequence.LastValue >= 2);
    }

    [Fact]
    public async Task ResubmissionRequiresSponsorSelfCheckAndKeepsTheNumber()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var task = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");

        using var revisionResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/revision",
            submitted.ETag,
            new PermitReasonRequest("Lengkapi nomor JSA.")));
        revisionResponse.EnsureSuccessStatusCode();
        var revision = Required(await revisionResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var withoutSelfCheck = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{revision.Id}/submit",
            revision.ETag,
            new SubmitPermitRequest(true, true, [])));
        Assert.Equal(HttpStatusCode.Conflict, withoutSelfCheck.StatusCode);
        Assert.Equal("permit.submit.revision_self_check_required", await ProblemCodeAsync(withoutSelfCheck));

        using var withSelfCheck = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{revision.Id}/submit",
            revision.ETag,
            new SubmitPermitRequest(true, true, [], RevisionSelfCheckConfirmed: true)));
        withSelfCheck.EnsureSuccessStatusCode();
        var resubmitted = Required(await withSelfCheck.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal(submitted.PermitNumber, resubmitted.PermitNumber);
        Assert.Equal(2, resubmitted.Version);
    }

    [Fact]
    public async Task HseSavesApdSeparatelyAndSelectionSurvivesRevisionUntilValidationWithSelfCheck()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var task = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");

        using var sponsorSave = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/safety-equipment",
            submitted.ETag,
            new SaveSafetyEquipmentRequest(["SAFETY_LOTO"])));
        Assert.Equal(HttpStatusCode.Forbidden, sponsorSave.StatusCode);

        using var saveResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/safety-equipment",
            submitted.ETag,
            new SaveSafetyEquipmentRequest(["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"])));
        saveResponse.EnsureSuccessStatusCode();
        var saved = Required(await saveResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("UNDER_VALIDATION", saved.Status);
        Assert.False(saved.Workflow.Hse.Completed);
        Assert.True(saved.Workflow.SafetyEquipmentSelection.Saved);
        Assert.Equal(
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            saved.Workflow.SafetyEquipmentSelection.SafetyEquipmentCodes);
        Assert.Equal("PENDING", (await PendingTaskAsync(saved.Id, "HSE_VALIDATION")).Status);

        using var revisionResponse = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/revision",
            saved.ETag,
            new PermitReasonRequest("Perbaiki uraian pekerjaan.")));
        revisionResponse.EnsureSuccessStatusCode();
        var revision = Required(await revisionResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.True(revision.Workflow.SafetyEquipmentSelection.Saved);

        using var resubmit = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{revision.Id}/submit",
            revision.ETag,
            new SubmitPermitRequest(true, true, [], RevisionSelfCheckConfirmed: true)));
        resubmit.EnsureSuccessStatusCode();
        var resubmitted = Required(await resubmit.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal(
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            resubmitted.Workflow.SafetyEquipmentSelection.SafetyEquipmentCodes);

        var newTask = await PendingTaskAsync(resubmitted.Id, "HSE_VALIDATION");
        using var withoutSelfCheck = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{newTask.Id}/validate",
            resubmitted.ETag,
            new ValidateSubmissionRequest("Revisi sesuai.", null)));
        Assert.Equal(HttpStatusCode.Conflict, withoutSelfCheck.StatusCode);
        Assert.Equal(
            "permit.validation.revision_self_check_required",
            await ProblemCodeAsync(withoutSelfCheck));

        using var validate = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{newTask.Id}/validate",
            resubmitted.ETag,
            new ValidateSubmissionRequest("Revisi sesuai.", null, RevisionSelfCheckConfirmed: true)));
        validate.EnsureSuccessStatusCode();
        var validated = Required(await validate.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_AREA_APPROVAL", validated.Status);
        Assert.True(validated.Workflow.Hse.RevisionSelfCheckConfirmed);
        Assert.Equal(
            ["SAFETY_FIRE_EXTINGUISHER", "SAFETY_LOTO"],
            validated.Workflow.Hse.SafetyEquipmentCodes);
    }

    [Fact]
    public async Task ActingManagerApprovalIsRecordedAsPjsAndExposedThroughMe()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var actingManager = Client(
            Unique("pjs-manager"),
            "AreaOwnerManager",
            "ORF",
            actingRoles: "AreaOwnerManager");

        var me = Required(await actingManager.GetFromJsonAsync<MeResponse>("/api/v1/me"));
        Assert.Contains("AreaOwnerManager", me.ActingRoles ?? []);

        var reviewed = await CreateValidateAndReviewAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(reviewed.Id, "AREA_APPROVE_AND_ISSUE");
        using var issue = await actingManager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            reviewed.ETag,
            new ApproveAndIssuePermitRequest("Disetujui sebagai PJS.", null)));
        issue.EnsureSuccessStatusCode();
        var issued = Required(await issue.Content.ReadFromJsonAsync<PermitResponse>());

        Assert.Equal("ISSUED", issued.Status);
        Assert.Equal("ACTING_FOR_MANAGER", issued.Workflow.Approval.Capacity);
        Assert.NotNull(issued.Workflow.Approval.ActingAssignmentId);
        Assert.NotEqual(issued.Workflow.Approval.ActorId, issued.Workflow.Approval.PrincipalManagerUserId);
    }

    [Fact]
    public async Task ClosureRecordsSection9AndAllowsIncompleteWorkToCloseWithStatus()
    {
        var sponsorId = Unique("sponsor");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");
        using var manager = Client(Unique("manager"), "AreaOwnerManager", "ORF");
        var reviewed = await CreateValidateAndReviewAsync(sponsor, validator, sponsorId);
        var approvalTask = await PendingTaskAsync(reviewed.Id, "AREA_APPROVE_AND_ISSUE");
        using var issue = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/approve-and-issue",
            reviewed.ETag,
            new ApproveAndIssuePermitRequest("Disetujui.", null)));
        issue.EnsureSuccessStatusCode();
        var issued = Required(await issue.Content.ReadFromJsonAsync<PermitResponse>());

        Guid printPackageId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
            var package = await db.PrintPackageSnapshots.SingleAsync(x => x.PermitId == issued.Id);
            package.RenderStatus = "READY";
            printPackageId = package.Id;
            await db.SaveChangesAsync();
        }

        var upload = await UploadSignedFieldCopyAsync(sponsor, issued, printPackageId);

        using var withoutSection9 = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests",
            upload.ETag,
            new RequestClosureRequest(printPackageId, [upload.Attachment.Id], "Selesai.", true, true)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutSection9.StatusCode);
        Assert.Equal("permit.closure.section9_required", await ProblemCodeAsync(withoutSection9));

        using var closureResponse = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{issued.Id}/closure-requests",
            upload.ETag,
            new RequestClosureRequest(
                printPackageId,
                [upload.Attachment.Id],
                "Sisa pekerjaan dilanjutkan pada PTW baru.",
                true,
                true,
                new PermitWorkCompletionRequest(
                    false,
                    true,
                    "Flange belum terpasang karena material terlambat.",
                    "Pelaksana Uji",
                    "B-001",
                    "S-001"))));
        closureResponse.EnsureSuccessStatusCode();
        var requested = Required(await closureResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.False(requested.Workflow.Closure.Completion!.WorkCompleted);
        Assert.Equal("B-001", requested.Workflow.Closure.Completion.PerformingAuthorityBadgeNumber);

        var closureTask = await PendingTaskAsync(issued.Id, "AREA_CLOSE_VERIFICATION");
        using var mismatch = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            requested.ETag,
            new ClosePermitRequest("Ditutup.", "Officer Operasi", true, true, true, true, true, true)));
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("permit.closure.completion_mismatch", await ProblemCodeAsync(mismatch));

        using var close = await manager.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/closure-tasks/{closureTask.Id}/close",
            requested.ETag,
            new ClosePermitRequest(
                "Ditutup dengan status pekerjaan belum selesai.",
                "Officer Operasi",
                true,
                false,
                false,
                true,
                true,
                true,
                "Flange belum terpasang; pekerjaan dilanjutkan pada PTW baru.")));
        close.EnsureSuccessStatusCode();
        var closed = Required(await close.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("CLOSED", closed.Status);
        Assert.False(closed.Workflow.Closure.WorkCompleted);
        Assert.Contains("Flange", closed.Workflow.Closure.IncompleteWorkStatus);

        var activity = Required(await manager.GetFromJsonAsync<PagedResponse<PermitActivityResponse>>(
            $"/api/v1/permits/{issued.Id}/activity?limit=20"));
        var closedEvent = Assert.Single(activity.Items, x => x.EventType == "permit_closed");
        Assert.True(closedEvent.Payload.TryGetProperty("section9", out var section9));
        Assert.False(section9.GetProperty("workCompleted").GetBoolean());
        Assert.Contains("Flange", closedEvent.Payload.GetProperty("incompleteWorkStatus").GetString());
    }

    [Fact]
    public async Task ContractorInitialPermitNeedsSponsorApprovalBeforeHseSubmissionAndStaysScoped()
    {
        var requesterId = Unique("requester");
        var sponsorId = Unique("sponsor");
        using var requester = Client(requesterId, "Requester", "*", "Kontraktor Uji");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var otherRequester = Client(Unique("requester"), "Requester", "*");

        // Sponsors never author a PTW: only the Contractor fills the initial PTW.
        using var sponsorCreate = await sponsor.PostAsJsonAsync("/api/v1/permits", Draft(sponsorId, "ORF"));
        Assert.Equal(HttpStatusCode.Forbidden, sponsorCreate.StatusCode);

        using var createResponse = await requester.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(sponsorId, "ORF") with { SubmitterType = "CONTRACTOR" });
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.True(draft.Workflow.Initiation.Initiated);
        Assert.Equal("DRAFTING", draft.Workflow.Initiation.Status);
        Assert.Equal(requesterId, draft.Workflow.Initiation.RequesterId);

        // The initiator sees its own draft; other Contractors do not.
        var mine = Required(await requester.GetFromJsonAsync<PagedResponse<PermitResponse>>("/api/v1/permits"));
        Assert.Contains(mine.Items, x => x.Id == draft.Id);
        var theirs = Required(
            await otherRequester.GetFromJsonAsync<PagedResponse<PermitResponse>>("/api/v1/permits"));
        Assert.DoesNotContain(theirs.Items, x => x.Id == draft.Id);
        using var foreignDetail = await otherRequester.GetAsync($"/api/v1/permits/{draft.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignDetail.StatusCode);

        draft = await UploadMandatoryDocumentsAsync(requester, draft);

        using var sponsorSubmitEarly = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/submit",
            draft.ETag,
            new SubmitPermitRequest(true, true, [])));
        Assert.Equal(HttpStatusCode.Conflict, sponsorSubmitEarly.StatusCode);
        Assert.Equal("permit.submit.initial_approval_required", await ProblemCodeAsync(sponsorSubmitEarly));

        using var handOver = await requester.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/initial-submit",
            draft.ETag,
            new { }));
        Assert.True(handOver.IsSuccessStatusCode, await handOver.Content.ReadAsStringAsync());
        var awaiting = Required(await handOver.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_SPONSOR_APPROVAL", awaiting.Workflow.Initiation.Status);
        Assert.Equal("DRAFT", awaiting.Status);

        var sponsorTasks = Required(await sponsor.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        var approvalTask = Assert.Single(
            sponsorTasks.Items,
            x => x.PermitId == draft.Id && x.Type == "SPONSOR_INITIAL_APPROVAL");

        // While the Sponsor reviews, the Contractor can no longer edit.
        using var lockedEdit = await requester.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            awaiting.ETag,
            awaiting.Draft with { Title = "Diubah saat ditinjau" },
            idempotencyKey: null));
        Assert.Equal(HttpStatusCode.Forbidden, lockedEdit.StatusCode);

        using var returned = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{approvalTask.Id}/return-initial",
            awaiting.ETag,
            new PermitReasonRequest("Lengkapi nama pelaksana.")));
        Assert.True(returned.IsSuccessStatusCode, await returned.Content.ReadAsStringAsync());
        var returnedPermit = Required(await returned.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("RETURNED", returnedPermit.Workflow.Initiation.Status);
        var contractorTasks = Required(
            await requester.GetFromJsonAsync<PagedResponse<PermitTaskResponse>>("/api/v1/tasks"));
        Assert.Contains(contractorTasks.Items, x => x.PermitId == draft.Id && x.Type == "REQUESTER_INITIAL_REVISION");

        using var handOverAgain = await requester.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/initial-submit",
            returnedPermit.ETag,
            new { }));
        Assert.True(handOverAgain.IsSuccessStatusCode, await handOverAgain.Content.ReadAsStringAsync());
        var awaitingAgain = Required(await handOverAgain.Content.ReadFromJsonAsync<PermitResponse>());
        var secondTask = await PendingTaskAsync(draft.Id, "SPONSOR_INITIAL_APPROVAL");

        // The Sponsor may edit what the Contractor submitted before approving it.
        using var sponsorEdit = await sponsor.SendAsync(Command(
            HttpMethod.Patch,
            $"/api/v1/permits/{draft.Id}/draft",
            awaitingAgain.ETag,
            awaitingAgain.Draft with { Title = "Judul diperbaiki Sponsor" },
            idempotencyKey: null));
        Assert.True(sponsorEdit.IsSuccessStatusCode, await sponsorEdit.Content.ReadAsStringAsync());
        var edited = Required(await sponsorEdit.Content.ReadFromJsonAsync<PermitResponse>());

        using var unconfirmed = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{secondTask.Id}/approve-initial",
            edited.ETag,
            new InitialApprovalRequest("PTW awal lengkap.")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unconfirmed.StatusCode);

        using var approve = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{secondTask.Id}/approve-initial",
            edited.ETag,
            new InitialApprovalRequest("PTW awal lengkap.", true)));
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
        var approved = Required(await approve.Content.ReadFromJsonAsync<PermitResponse>());

        // Approval is the submission: status, official number, and HSE task come from one command.
        Assert.Equal("APPROVED", approved.Workflow.Initiation.Status);
        Assert.Equal("UNDER_VALIDATION", approved.Status);
        Assert.Equal("Judul diperbaiki Sponsor", approved.Draft.Title);
        Assert.StartsWith("PTW/", approved.PermitNumber, StringComparison.Ordinal);
        _ = await PendingTaskAsync(draft.Id, "HSE_VALIDATION");
    }

    [Fact]
    public async Task AnyAccountWithTheRequesterRoleCanAuthorForAnotherSponsor()
    {
        var authorId = Unique("staff");
        var sponsorId = Unique("sponsor");
        using var author = Client(authorId, "Sponsor,Requester", "ORF");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");

        using var selfSponsored = await author.PostAsJsonAsync("/api/v1/permits", Draft(authorId, "ORF"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selfSponsored.StatusCode);
        Assert.Equal("permit.initiation.sponsor_required", await ProblemCodeAsync(selfSponsored));

        using var created = await author.PostAsJsonAsync("/api/v1/permits", Draft(sponsorId, "ORF"));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var draft = Required(await created.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal(authorId, draft.Workflow.Initiation.RequesterId);

        // The author sees the PTW it initiated even though it is not the Sponsor.
        var mine = Required(await author.GetFromJsonAsync<PagedResponse<PermitResponse>>("/api/v1/permits"));
        Assert.Contains(mine.Items, x => x.Id == draft.Id);
        using var detail = await author.GetAsync($"/api/v1/permits/{draft.Id}");
        detail.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SponsorCanRejectTheContractorInitialPermit()
    {
        var requesterId = Unique("requester");
        var sponsorId = Unique("sponsor");
        using var requester = Client(requesterId, "Requester", "*");
        using var sponsor = Client(sponsorId, "Sponsor", "ORF");
        using var createResponse = await requester.PostAsJsonAsync("/api/v1/permits", Draft(sponsorId, "ORF"));
        createResponse.EnsureSuccessStatusCode();
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        draft = await UploadMandatoryDocumentsAsync(requester, draft);
        using var handOver = await requester.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/initial-submit",
            draft.ETag,
            new { }));
        handOver.EnsureSuccessStatusCode();
        var awaiting = Required(await handOver.Content.ReadFromJsonAsync<PermitResponse>());
        var task = await PendingTaskAsync(draft.Id, "SPONSOR_INITIAL_APPROVAL");

        using var requesterReject = await requester.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/reject-initial",
            awaiting.ETag,
            new PermitReasonRequest("Coba tolak sendiri.")));
        Assert.Equal(HttpStatusCode.Forbidden, requesterReject.StatusCode);

        using var reject = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/reject-initial",
            awaiting.ETag,
            new PermitReasonRequest("Pekerjaan tidak sesuai rencana operasi.")));
        Assert.True(reject.IsSuccessStatusCode, await reject.Content.ReadAsStringAsync());
        var rejected = Required(await reject.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal("REJECTED", rejected.Workflow.Initiation.Status);
        Assert.Null(rejected.PermitNumber);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Empty(await db.PermitTasks.Where(x => x.PermitId == draft.Id && x.Status == "PENDING").ToListAsync());
    }

    [Fact]
    public async Task AttachmentPolicyAndSponsorListAreScopedReferenceData()
    {
        using var requester = Client(Unique("requester"), "Requester", "*");
        using var validator = Client(Unique("hse"), "HSEValidator", "ORF");

        var policy = Required(await requester.GetFromJsonAsync<AttachmentPolicyResponse>(
            "/api/v1/reference-data/attachment-policy"));
        Assert.True(policy.MaxFileBytes > 0);
        Assert.Equal(20, policy.MaxFilesPerPermit);
        Assert.Contains("application/pdf", policy.AcceptedMediaTypes);

        using var sponsors = await requester.GetAsync("/api/v1/sponsors");
        sponsors.EnsureSuccessStatusCode();
        using var forbidden = await validator.GetAsync("/api/v1/sponsors");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var mandatory = Required(await requester.GetFromJsonAsync<PermitMandatoryDocumentOptionResponse[]>(
            "/api/v1/reference-data/mandatory-documents"));
        Assert.DoesNotContain(mandatory, x => x.Code == "ESIMI");
    }

    private async Task<PermitResponse> CreateValidateAndReviewAsync(
        HttpClient sponsor,
        HttpClient validator,
        string sponsorId)
    {
        var submitted = await CreateAndSubmitAsync(sponsor, sponsorId);
        var task = await PendingTaskAsync(submitted.Id, "HSE_VALIDATION");
        using var validate = await validator.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/validate",
            submitted.ETag,
            new ValidateSubmissionRequest("Valid.", ["SAFETY_FIRE_EXTINGUISHER"])));
        validate.EnsureSuccessStatusCode();
        var validated = Required(await validate.Content.ReadFromJsonAsync<PermitResponse>());

        using var seniorOfficer = Client(Unique("senior-officer"), "AreaOwnerSeniorOfficer", "ORF");
        var areaTask = await PendingTaskAsync(validated.Id, "AREA_OPERATION_REVIEW");
        using var review = await seniorOfficer.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{areaTask.Id}/review-area-operations",
            validated.ETag,
            new ReviewAreaOperationsRequest(
                "Kondisi operasi diperiksa.",
                [PermitOperationalConditionCatalog.Depressurized],
                null,
                true)));
        review.EnsureSuccessStatusCode();
        return Required(await review.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private HttpClient RequesterFor(string sponsorId) =>
        Client($"{sponsorId}.requester", "Requester", "*", "Kontraktor Uji");

    /// <summary>Contractor fills and hands over; the Sponsor's approval submits to the PIC HSE.</summary>
    private async Task<PermitResponse> CreateAndSubmitAsync(HttpClient sponsor, string sponsorId)
    {
        var requester = RequesterFor(sponsorId);
        using var createResponse = await requester.PostAsJsonAsync("/api/v1/permits", Draft(sponsorId, "ORF"));
        Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
        var draft = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());
        draft = await UploadMandatoryDocumentsAsync(requester, draft);
        using var handOver = await requester.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/permits/{draft.Id}/initial-submit",
            draft.ETag,
            new { }));
        Assert.True(handOver.IsSuccessStatusCode, await handOver.Content.ReadAsStringAsync());
        var awaiting = Required(await handOver.Content.ReadFromJsonAsync<PermitResponse>());
        var task = await PendingTaskAsync(draft.Id, "SPONSOR_INITIAL_APPROVAL");
        using var response = await sponsor.SendAsync(Command(
            HttpMethod.Post,
            $"/api/v1/tasks/{task.Id}/approve-initial",
            awaiting.ETag,
            new InitialApprovalRequest("PTW awal disetujui.", true)));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Required(await response.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private static async Task<PermitResponse> UploadMandatoryDocumentsAsync(HttpClient client, PermitResponse permit)
    {
        permit = await UploadAsync(client, permit, "JSA", "JSA", permit.Draft.JsaDocumentNumber, permit.Draft.JsaRevision, permit.Draft.JsaDate);
        foreach (var code in new[] { "WORK_PROCEDURE", "ID", "BPJS_TK", "FTW" })
        {
            permit = await UploadAsync(client, permit, "SUPPORTING", code, null, null, null);
        }

        return permit;
    }

    private static async Task<PermitAttachmentMutationResponse> UploadSignedFieldCopyAsync(
        HttpClient client,
        PermitResponse permit,
        Guid printPackageId)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "signed-field-copy.pdf");
        content.Add(new StringContent("SIGNED_FIELD_COPY"), "category");
        content.Add(new StringContent("SFC-001"), "documentNumber");
        content.Add(new StringContent("1"), "documentRevision");
        content.Add(new StringContent(DateTimeOffset.UtcNow.ToString("O")), "documentDate");
        content.Add(new StringContent(printPackageId.ToString()), "printPackageId");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Required(await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
    }

    private static async Task<PermitResponse> UploadAsync(
        HttpClient client,
        PermitResponse permit,
        string category,
        string documentCode,
        string? documentNumber,
        string? documentRevision,
        DateTimeOffset? documentDate)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", $"{documentCode.ToLowerInvariant()}.pdf");
        content.Add(new StringContent(category), "category");
        content.Add(new StringContent(documentCode), "supportingDocumentCode");
        if (documentNumber is not null)
        {
            content.Add(new StringContent(documentNumber), "documentNumber");
            content.Add(new StringContent(Required(documentRevision)), "documentRevision");
            content.Add(new StringContent(documentDate!.Value.ToString("O")), "documentDate");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permits/{permit.Id}/attachments")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("If-Match", permit.ETag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var mutation = Required(await response.Content.ReadFromJsonAsync<PermitAttachmentMutationResponse>());
        return permit with { ETag = mutation.ETag, Version = mutation.PermitVersion };
    }

    private async Task<PermitTaskRecord> PendingTaskAsync(Guid permitId, string type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        return await db.PermitTasks.AsNoTracking().SingleAsync(
            x => x.PermitId == permitId && x.Type == type && x.Status == "PENDING");
    }

    private HttpClient Client(
        string userId,
        string roles,
        string locations,
        string? displayName = null,
        string? actingRoles = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", displayName ?? userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        client.DefaultRequestHeaders.Add("X-Dev-Locations", locations);
        if (actingRoles is not null)
        {
            client.DefaultRequestHeaders.Add("X-Dev-Acting-Roles", actingRoles);
        }

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
            [],
            [],
            ["JSA", "WORK_PROCEDURE"],
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
