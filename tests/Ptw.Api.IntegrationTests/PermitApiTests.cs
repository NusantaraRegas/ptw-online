using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ptw.Contracts;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class PermitApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task UpdateDraftWithStaleETagReturnsConflict()
    {
        using var client = factory.CreateClient();
        var created = await CreatePermitAsync(client, "AREA-CONCURRENCY");

        using var update = PatchDraft(created.Id, created.ETag, Draft("AREA-CONCURRENCY", "Versi terbaru"));
        using var updatedResponse = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<PermitResponse>();
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);

        using var staleUpdate = PatchDraft(created.Id, created.ETag, Draft("AREA-CONCURRENCY", "Versi stale"));
        using var conflict = await client.SendAsync(staleUpdate);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("concurrency.conflict", await ProblemCodeAsync(conflict));
    }

    [Fact]
    public async Task GetPermitOutsideLocationScopeReturnsForbidden()
    {
        using var ownerClient = factory.CreateClient();
        var created = await CreatePermitAsync(ownerClient, "AREA-OWNER");

        using var scopedClient = factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Add("X-Dev-Locations", "AREA-OTHER");
        using var detailResponse = await scopedClient.GetAsync($"/api/v1/permits/{created.Id}");
        using var activityResponse = await scopedClient.GetAsync($"/api/v1/permits/{created.Id}/activity");
        using var versionsResponse = await scopedClient.GetAsync($"/api/v1/permits/{created.Id}/versions");

        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(detailResponse));
        Assert.Equal(HttpStatusCode.Forbidden, activityResponse.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(activityResponse));
        Assert.Equal(HttpStatusCode.Forbidden, versionsResponse.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(versionsResponse));
    }

    [Fact]
    public async Task HistoryIsAppendOnlyOrderedAndPaginated()
    {
        using var client = factory.CreateClient();
        var created = await CreatePermitAsync(client, "AREA-HISTORY");

        using var initialActivityResponse = await client.GetAsync(
            $"/api/v1/permits/{created.Id}/activity?offset=0&limit=1");
        initialActivityResponse.EnsureSuccessStatusCode();
        var initialActivity = await initialActivityResponse.Content.ReadFromJsonAsync<PagedResponse<PermitActivityResponse>>();
        var initialEvent = Assert.Single(Assert.IsType<PagedResponse<PermitActivityResponse>>(initialActivity).Items);
        Assert.Equal(1, initialActivity.Count);

        var versionTwo = await UpdatePermitAsync(client, created, "Draft versi dua");
        var versionThree = await UpdatePermitAsync(client, versionTwo, "Draft versi tiga");

        using var latestActivityResponse = await client.GetAsync(
            $"/api/v1/permits/{created.Id}/activity?offset=0&limit=2");
        latestActivityResponse.EnsureSuccessStatusCode();
        var latestActivity = Assert.IsType<PagedResponse<PermitActivityResponse>>(
            await latestActivityResponse.Content.ReadFromJsonAsync<PagedResponse<PermitActivityResponse>>());
        Assert.Equal(3, latestActivity.Count);
        Assert.Equal(2, latestActivity.Items.Count);
        Assert.True(latestActivity.Items[0].Sequence > latestActivity.Items[1].Sequence);

        using var oldestActivityResponse = await client.GetAsync(
            $"/api/v1/permits/{created.Id}/activity?offset=2&limit=2");
        oldestActivityResponse.EnsureSuccessStatusCode();
        var oldestActivity = Assert.IsType<PagedResponse<PermitActivityResponse>>(
            await oldestActivityResponse.Content.ReadFromJsonAsync<PagedResponse<PermitActivityResponse>>());
        var persistedInitialEvent = Assert.Single(oldestActivity.Items);
        Assert.Equal(3, oldestActivity.Count);
        Assert.Equal(initialEvent.Sequence, persistedInitialEvent.Sequence);
        Assert.Equal(initialEvent.EventType, persistedInitialEvent.EventType);
        Assert.Equal(initialEvent.ActorId, persistedInitialEvent.ActorId);
        Assert.Equal(initialEvent.Payload.GetRawText(), persistedInitialEvent.Payload.GetRawText());

        using var latestVersionsResponse = await client.GetAsync(
            $"/api/v1/permits/{created.Id}/versions?offset=0&limit=2");
        latestVersionsResponse.EnsureSuccessStatusCode();
        var latestVersions = Assert.IsType<PagedResponse<PermitVersionResponse>>(
            await latestVersionsResponse.Content.ReadFromJsonAsync<PagedResponse<PermitVersionResponse>>());
        Assert.Equal(3, latestVersions.Count);
        Assert.Collection(
            latestVersions.Items,
            item =>
            {
                Assert.Equal(3, item.Version);
                Assert.Equal("Draft versi tiga", item.Snapshot.Title);
            },
            item =>
            {
                Assert.Equal(2, item.Version);
                Assert.Equal("Draft versi dua", item.Snapshot.Title);
            });

        using var oldestVersionResponse = await client.GetAsync(
            $"/api/v1/permits/{created.Id}/versions?offset=2&limit=2");
        oldestVersionResponse.EnsureSuccessStatusCode();
        var oldestVersions = Assert.IsType<PagedResponse<PermitVersionResponse>>(
            await oldestVersionResponse.Content.ReadFromJsonAsync<PagedResponse<PermitVersionResponse>>());
        var originalVersion = Assert.Single(oldestVersions.Items);
        Assert.Equal(1, originalVersion.Version);
        Assert.Equal("Draft awal", originalVersion.Snapshot.Title);
        Assert.False(string.IsNullOrWhiteSpace(originalVersion.ContentHash));
        Assert.Equal(versionThree.Version, latestVersions.Items[0].Version);
    }

    [Fact]
    public async Task HistoryRejectsInvalidPagination()
    {
        using var client = factory.CreateClient();
        var created = await CreatePermitAsync(client, "AREA-PAGINATION");

        using var invalidOffset = await client.GetAsync($"/api/v1/permits/{created.Id}/activity?offset=-1");
        using var invalidLimit = await client.GetAsync($"/api/v1/permits/{created.Id}/versions?limit=101");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidOffset.StatusCode);
        Assert.Equal("pagination.invalid_offset", await ProblemCodeAsync(invalidOffset));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidLimit.StatusCode);
        Assert.Equal("pagination.invalid_limit", await ProblemCodeAsync(invalidLimit));
    }

    [Fact]
    public async Task SubmitReplaysSameIdempotencyKeyAndRejectsDifferentPayload()
    {
        using var client = factory.CreateClient();
        var created = await CreatePermitAsync(client, "AREA-IDEMPOTENCY");
        var request = new SubmitPermitRequest(true, true, true, []);
        var key = Guid.NewGuid().ToString("N");

        using var first = Submit(created.Id, created.ETag, key, request);
        using var firstResponse = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var submitted = await firstResponse.Content.ReadFromJsonAsync<PermitResponse>();
        Assert.NotNull(submitted);
        Assert.Equal("UNDER_REVIEW", submitted.Status);
        Assert.False(submitted.Workflow.Hsse.Completed);
        Assert.False(submitted.Workflow.GasDistribution.Completed);

        using var replay = Submit(created.Id, created.ETag, key, request);
        using var replayResponse = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayed = await replayResponse.Content.ReadFromJsonAsync<PermitResponse>();
        Assert.Equal(submitted.Id, replayed?.Id);
        Assert.Equal(submitted.Version, replayed?.Version);

        using var mismatch = Submit(
            created.Id,
            created.ETag,
            key,
            request with { RequiredDocumentsSafe = false });
        using var mismatchResponse = await client.SendAsync(mismatch);
        Assert.Equal(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(mismatchResponse));
    }

    [Fact]
    public async Task HsseValidationGatesAreaApprovalAndIssuance()
    {
        const string location = "ORF";
        using var sponsor = factory.CreateClient();
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(location, "Flow validasi HSSE") with
            {
                ValidFrom = DateTimeOffset.UtcNow.AddMinutes(-5),
                ValidUntil = DateTimeOffset.UtcNow.AddHours(8)
            });
        createResponse.EnsureSuccessStatusCode();
        var created = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var submit = Submit(
            created.Id,
            created.ETag,
            Guid.NewGuid().ToString("N"),
            new SubmitPermitRequest(true, true, true, []));
        using var submitResponse = await sponsor.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var underReview = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("UNDER_REVIEW", underReview.Status);

        using var hsse = WorkflowClient("hsse.validator", "HSSEValidator", "*");
        using var hsseValidation = WorkflowCommand(
            underReview.Id,
            "validations/hsse/endorse",
            underReview.ETag,
            new EndorsePermitValidationRequest("Persyaratan HSSE sesuai."));
        using var hsseResponse = await hsse.SendAsync(hsseValidation);
        hsseResponse.EnsureSuccessStatusCode();
        var awaitingApproval = Required(await hsseResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_APPROVAL", awaitingApproval.Status);
        Assert.True(awaitingApproval.Workflow.Hsse.Completed);
        Assert.False(awaitingApproval.Workflow.GasDistribution.Completed);

        using var outsideAreaOwner = WorkflowClient(
            "area.owner.other",
            "AreaOwnerApprover",
            "HO");
        using var outsideApproval = WorkflowCommand(
            awaitingApproval.Id,
            "approve",
            awaitingApproval.ETag,
            new ApprovePermitRequest("Approval di luar area."));
        using var outsideApprovalResponse = await outsideAreaOwner.SendAsync(outsideApproval);
        Assert.Equal(HttpStatusCode.Forbidden, outsideApprovalResponse.StatusCode);

        using var areaOwner = WorkflowClient(
            "area.owner.orf",
            "AreaOwnerApprover,IssuingAuthority",
            location);
        using var approval = WorkflowCommand(
            awaitingApproval.Id,
            "approve",
            awaitingApproval.ETag,
            new ApprovePermitRequest("Disetujui pemilik area ORF."));
        using var approvalResponse = await areaOwner.SendAsync(approval);
        approvalResponse.EnsureSuccessStatusCode();
        var approved = Required(await approvalResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("APPROVED", approved.Status);
        Assert.Null(approved.ActiveWorkPeriodId);

        var ready = new IssuePermitRequest(true, true, true, true, true, true, true, true, false);
        using var failedIssue = WorkflowCommand(
            approved.Id,
            "issue",
            approved.ETag,
            ready with { GasTestSatisfied = false });
        using var failedIssueResponse = await areaOwner.SendAsync(failedIssue);
        Assert.Equal(HttpStatusCode.Conflict, failedIssueResponse.StatusCode);
        Assert.Equal("permit.issue.guards_failed", await ProblemCodeAsync(failedIssueResponse));

        using var issue = WorkflowCommand(
            approved.Id,
            "issue",
            approved.ETag,
            ready);
        using var issueResponse = await areaOwner.SendAsync(issue);
        issueResponse.EnsureSuccessStatusCode();
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("OPEN", issued.Status);
        Assert.NotNull(issued.ActiveWorkPeriodId);
    }

    [Fact]
    public async Task WorkflowTasksRouteHsseValidationAndBindIssueToApprover()
    {
        var location = $"AREA-TASK-{Guid.NewGuid():N}";
        using var sponsor = factory.CreateClient();
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(location, "Task workflow") with
            {
                ValidFrom = DateTimeOffset.UtcNow.AddMinutes(-5),
                ValidUntil = DateTimeOffset.UtcNow.AddHours(8)
            });
        createResponse.EnsureSuccessStatusCode();
        var created = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var submit = Submit(
            created.Id,
            created.ETag,
            Guid.NewGuid().ToString("N"),
            new SubmitPermitRequest(true, true, true, []));
        using var submitResponse = await sponsor.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var underReview = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var validator = WorkflowClient(
            "hsse.validator.task",
            "HSSEValidator",
            location);
        var validationTasks = await GetTasksAsync(validator);
        Assert.Equal("HSSE_VALIDATION", Assert.Single(validationTasks.Items).Type);

        using var hsse = WorkflowCommand(
            underReview.Id,
            "validations/hsse/endorse",
            underReview.ETag,
            new EndorsePermitValidationRequest("Validasi HSSE oleh pemegang multi-role."));
        using var hsseResponse = await validator.SendAsync(hsse);
        hsseResponse.EnsureSuccessStatusCode();
        var awaitingApproval = Required(await hsseResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_APPROVAL", awaitingApproval.Status);
        Assert.Empty((await GetTasksAsync(validator)).Items);

        using var approver = WorkflowClient("area.owner.primary", "AreaOwnerApprover", location);
        Assert.Equal("AREA_OWNER_APPROVAL", Assert.Single((await GetTasksAsync(approver)).Items).Type);
        using var approval = WorkflowCommand(
            awaitingApproval.Id,
            "approve",
            awaitingApproval.ETag,
            new ApprovePermitRequest("Disetujui PIC pemilik area."));
        using var approvalResponse = await approver.SendAsync(approval);
        approvalResponse.EnsureSuccessStatusCode();
        var approved = Required(await approvalResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AREA_OWNER_ISSUE", Assert.Single((await GetTasksAsync(approver)).Items).Type);

        var ready = new IssuePermitRequest(true, true, true, true, true, true, true, true, false);
        using var otherAreaOwner = WorkflowClient("area.owner.other", "AreaOwnerApprover", location);
        Assert.Empty((await GetTasksAsync(otherAreaOwner)).Items);
        using var deniedIssue = WorkflowCommand(approved.Id, "issue", approved.ETag, ready);
        using var deniedResponse = await otherAreaOwner.SendAsync(deniedIssue);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var issue = WorkflowCommand(approved.Id, "issue", approved.ETag, ready);
        using var issueResponse = await approver.SendAsync(issue);
        issueResponse.EnsureSuccessStatusCode();
        Assert.Empty((await GetTasksAsync(approver)).Items);
    }

    [Fact]
    public async Task SponsorSuspensionRequestImmediatelyStopsWorkAndRequiresAreaOwnerApproval()
    {
        var location = $"AREA-SUSPEND-{Guid.NewGuid():N}";
        using var sponsor = factory.CreateClient();
        using var hsse = WorkflowClient("hsse.suspension", "HSSEValidator", location);
        using var areaOwner = WorkflowClient("area.owner.suspension", "AreaOwnerApprover", location);
        var open = await CreateOpenPermitAsync(sponsor, hsse, areaOwner, location);

        using var unauthorizedRequest = WorkflowCommand(
            open.Id,
            "suspensions/request",
            open.ETag,
            new PermitReasonRequest("Permintaan oleh pihak yang bukan Sponsor."));
        using var unauthorizedResponse = await hsse.SendAsync(unauthorizedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);

        var key = Guid.NewGuid().ToString("N");
        var reason = new PermitReasonRequest("Kondisi lapangan berubah dan pekerjaan harus dihentikan.");
        using var request = WorkflowCommand(open.Id, "suspensions/request", open.ETag, reason, key);
        using var requestResponse = await sponsor.SendAsync(request);
        requestResponse.EnsureSuccessStatusCode();
        var requested = Required(await requestResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("SUSPENSION_REQUESTED", requested.Status);
        Assert.Null(requested.ActiveWorkPeriodId);
        Assert.True(requested.Workflow.Suspension.Requested);
        Assert.False(requested.Workflow.Suspension.Approved);
        Assert.Equal("SUSPENSION_APPROVAL", Assert.Single((await GetTasksAsync(areaOwner)).Items).Type);

        using var replay = WorkflowCommand(open.Id, "suspensions/request", open.ETag, reason, key);
        using var replayResponse = await sponsor.SendAsync(replay);
        replayResponse.EnsureSuccessStatusCode();
        Assert.Equal(
            requested.Version,
            (await replayResponse.Content.ReadFromJsonAsync<PermitResponse>())?.Version);

        using var sponsorApproval = WorkflowCommand(
            requested.Id,
            "suspensions/approve",
            requested.ETag,
            new ConfirmPermitActionRequest("Sponsor tidak boleh menyetujui sendiri."));
        using var sponsorApprovalResponse = await sponsor.SendAsync(sponsorApproval);
        Assert.Equal(HttpStatusCode.Forbidden, sponsorApprovalResponse.StatusCode);

        using var approval = WorkflowCommand(
            requested.Id,
            "suspensions/approve",
            requested.ETag,
            new ConfirmPermitActionRequest("Pemilik area menyetujui penangguhan."));
        using var approvalResponse = await areaOwner.SendAsync(approval);
        approvalResponse.EnsureSuccessStatusCode();
        var suspended = Required(await approvalResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("SUSPENDED", suspended.Status);
        Assert.Null(suspended.ActiveWorkPeriodId);
        Assert.True(suspended.Workflow.Suspension.Approved);
        Assert.Empty((await GetTasksAsync(areaOwner)).Items);
    }

    [Fact]
    public async Task CompletionRequiresSponsorHsseAndAreaOwnerBeforeAreaOwnerCanClose()
    {
        var location = $"AREA-COMPLETE-{Guid.NewGuid():N}";
        using var sponsor = factory.CreateClient();
        using var hsse = WorkflowClient("hsse.completion", "HSSEValidator", location);
        using var areaOwner = WorkflowClient("area.owner.completion", "AreaOwnerApprover", location);
        var open = await CreateOpenPermitAsync(sponsor, hsse, areaOwner, location);

        using var earlyClose = WorkflowCommand(
            open.Id,
            "close",
            open.ETag,
            new ConfirmPermitActionRequest("Belum ada konfirmasi penyelesaian."));
        using var earlyCloseResponse = await areaOwner.SendAsync(earlyClose);
        Assert.Equal(HttpStatusCode.Conflict, earlyCloseResponse.StatusCode);
        Assert.Equal("permit.invalid_transition", await ProblemCodeAsync(earlyCloseResponse));

        using var declaration = WorkflowCommand(
            open.Id,
            "completion/declare",
            open.ETag,
            new ConfirmPermitActionRequest("Pekerjaan telah selesai dan area siap diperiksa."));
        using var declarationResponse = await sponsor.SendAsync(declaration);
        declarationResponse.EnsureSuccessStatusCode();
        var pending = Required(await declarationResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("COMPLETION_CONFIRMATION_PENDING", pending.Status);
        Assert.Null(pending.ActiveWorkPeriodId);
        Assert.True(pending.Workflow.Completion.Sponsor.Completed);
        Assert.False(pending.Workflow.Completion.Hsse.Completed);
        Assert.False(pending.Workflow.Completion.AreaOwner.Completed);
        Assert.Equal("HSSE_COMPLETION_CONFIRMATION", Assert.Single((await GetTasksAsync(hsse)).Items).Type);
        Assert.Equal("AREA_OWNER_COMPLETION_CONFIRMATION", Assert.Single((await GetTasksAsync(areaOwner)).Items).Type);

        using var areaConfirmation = WorkflowCommand(
            pending.Id,
            "completion/confirm/area-owner",
            pending.ETag,
            new ConfirmPermitActionRequest("Area telah diperiksa oleh pemilik area."));
        using var areaConfirmationResponse = await areaOwner.SendAsync(areaConfirmation);
        areaConfirmationResponse.EnsureSuccessStatusCode();
        var awaitingHsse = Required(await areaConfirmationResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("COMPLETION_CONFIRMATION_PENDING", awaitingHsse.Status);
        Assert.True(awaitingHsse.Workflow.Completion.AreaOwner.Completed);
        Assert.False(awaitingHsse.Workflow.Completion.Hsse.Completed);
        Assert.Empty((await GetTasksAsync(areaOwner)).Items);

        using var stillEarlyClose = WorkflowCommand(
            awaitingHsse.Id,
            "close",
            awaitingHsse.ETag,
            new ConfirmPermitActionRequest("HSSE belum mengonfirmasi."));
        using var stillEarlyCloseResponse = await areaOwner.SendAsync(stillEarlyClose);
        Assert.Equal(HttpStatusCode.Conflict, stillEarlyCloseResponse.StatusCode);

        using var hsseConfirmation = WorkflowCommand(
            awaitingHsse.Id,
            "completion/confirm/hsse",
            awaitingHsse.ETag,
            new ConfirmPermitActionRequest("Kondisi akhir pekerjaan aman."));
        using var hsseConfirmationResponse = await hsse.SendAsync(hsseConfirmation);
        hsseConfirmationResponse.EnsureSuccessStatusCode();
        var completed = Required(await hsseConfirmationResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("WORK_COMPLETED", completed.Status);
        Assert.True(completed.Workflow.Completion.Hsse.Completed);
        Assert.True(completed.Workflow.Completion.AreaOwner.Completed);
        Assert.Empty((await GetTasksAsync(hsse)).Items);
        Assert.Equal("AREA_OWNER_CLOSE", Assert.Single((await GetTasksAsync(areaOwner)).Items).Type);

        using var hsseClose = WorkflowCommand(
            completed.Id,
            "close",
            completed.ETag,
            new ConfirmPermitActionRequest("HSSE tidak berwenang menutup."));
        using var hsseCloseResponse = await hsse.SendAsync(hsseClose);
        Assert.Equal(HttpStatusCode.Forbidden, hsseCloseResponse.StatusCode);

        using var close = WorkflowCommand(
            completed.Id,
            "close",
            completed.ETag,
            new ConfirmPermitActionRequest("PIC pemilik area menutup PTW."));
        using var closeResponse = await areaOwner.SendAsync(close);
        closeResponse.EnsureSuccessStatusCode();
        var closed = Required(await closeResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("CLOSED", closed.Status);
        Assert.Empty((await GetTasksAsync(areaOwner)).Items);
    }

    [Fact]
    public async Task ValidatorCanRequestRevisionAndResubmissionCreatesFreshVersionTasks()
    {
        var location = $"AREA-REVISION-{Guid.NewGuid():N}";
        using var sponsor = factory.CreateClient();
        var created = await CreatePermitAsync(sponsor, location);
        using var submit = Submit(
            created.Id,
            created.ETag,
            Guid.NewGuid().ToString("N"),
            new SubmitPermitRequest(true, true, true, []));
        using var submitResponse = await sponsor.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var underReview = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var areaOwner = WorkflowClient("area.owner.early", "AreaOwnerApprover", location);
        using var deniedRevision = WorkflowCommand(
            underReview.Id,
            "request-revision",
            underReview.ETag,
            new PermitReasonRequest("Area owner belum berada pada tahap approval."));
        using var deniedRevisionResponse = await areaOwner.SendAsync(deniedRevision);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRevisionResponse.StatusCode);

        using var validator = WorkflowClient("hsse.reviewer", "HSSEValidator", location);
        var key = Guid.NewGuid().ToString("N");
        var reason = new PermitReasonRequest("Kontrol isolasi berubah material.");
        using var revision = WorkflowCommand(
            underReview.Id,
            "request-revision",
            underReview.ETag,
            reason,
            key);
        using var revisionResponse = await validator.SendAsync(revision);
        revisionResponse.EnsureSuccessStatusCode();
        var revisionRequired = Required(await revisionResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("REVISION_REQUIRED", revisionRequired.Status);
        Assert.False(revisionRequired.Workflow.Hsse.Completed);
        Assert.False(revisionRequired.Workflow.GasDistribution.Completed);
        Assert.Empty((await GetTasksAsync(validator)).Items);

        using var replay = WorkflowCommand(
            underReview.Id,
            "request-revision",
            underReview.ETag,
            reason,
            key);
        using var replayResponse = await validator.SendAsync(replay);
        replayResponse.EnsureSuccessStatusCode();
        Assert.Equal(
            revisionRequired.Status,
            (await replayResponse.Content.ReadFromJsonAsync<PermitResponse>())?.Status);

        var revised = await UpdatePermitAsync(sponsor, revisionRequired, "Draft setelah revisi material");
        Assert.Equal(2, revised.Version);
        using var resubmit = Submit(
            revised.Id,
            revised.ETag,
            Guid.NewGuid().ToString("N"),
            new SubmitPermitRequest(true, true, true, []));
        using var resubmitResponse = await sponsor.SendAsync(resubmit);
        resubmitResponse.EnsureSuccessStatusCode();
        using var multiReviewer = WorkflowClient(
            "multi.reviewer",
            "HSSEValidator",
            location);
        var freshTasks = await GetTasksAsync(multiReviewer);
        Assert.Single(freshTasks.Items);
        Assert.All(freshTasks.Items, task => Assert.Equal(2, task.PermitVersion));
    }

    [Fact]
    public async Task OnlyAreaOwnerCanRejectAtApprovalAndRejectionIsTerminalAndIdempotent()
    {
        var location = $"AREA-REJECT-{Guid.NewGuid():N}";
        using var sponsor = factory.CreateClient();
        var created = await CreatePermitAsync(sponsor, location);
        using var submit = Submit(
            created.Id,
            created.ETag,
            Guid.NewGuid().ToString("N"),
            new SubmitPermitRequest(true, true, true, []));
        using var submitResponse = await sponsor.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var current = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var validator = WorkflowClient(
            "multi.reject.reviewer",
            "HSSEValidator",
            location);
        using var hsse = WorkflowCommand(
            current.Id,
            "validations/hsse/endorse",
            current.ETag,
            new EndorsePermitValidationRequest("HSSE sesuai."));
        using var hsseResponse = await validator.SendAsync(hsse);
        hsseResponse.EnsureSuccessStatusCode();
        current = Required(await hsseResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_APPROVAL", current.Status);

        using var validatorReject = WorkflowCommand(
            current.Id,
            "reject",
            current.ETag,
            new PermitReasonRequest("Validator tidak berwenang pada tahap approval."));
        using var validatorRejectResponse = await validator.SendAsync(validatorReject);
        Assert.Equal(HttpStatusCode.Forbidden, validatorRejectResponse.StatusCode);

        using var areaOwner = WorkflowClient("area.owner.reject", "AreaOwnerApprover", location);
        var key = Guid.NewGuid().ToString("N");
        var reason = new PermitReasonRequest("Risiko residual tidak dapat diterima.");
        using var reject = WorkflowCommand(current.Id, "reject", current.ETag, reason, key);
        using var rejectResponse = await areaOwner.SendAsync(reject);
        rejectResponse.EnsureSuccessStatusCode();
        var rejected = Required(await rejectResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Empty((await GetTasksAsync(areaOwner)).Items);

        using var replay = WorkflowCommand(current.Id, "reject", current.ETag, reason, key);
        using var replayResponse = await areaOwner.SendAsync(replay);
        replayResponse.EnsureSuccessStatusCode();
        using var mismatch = WorkflowCommand(
            current.Id,
            "reject",
            current.ETag,
            reason with { Reason = "Alasan berbeda." },
            key);
        using var mismatchResponse = await areaOwner.SendAsync(mismatch);
        Assert.Equal(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(mismatchResponse));

        using var updateRejected = PatchDraft(rejected.Id, rejected.ETag, Draft(location, "Tidak boleh diubah"));
        using var updateRejectedResponse = await sponsor.SendAsync(updateRejected);
        Assert.Equal(HttpStatusCode.Conflict, updateRejectedResponse.StatusCode);
        Assert.Equal("permit.invalid_transition", await ProblemCodeAsync(updateRejectedResponse));
    }

    private static async Task<PermitResponse> CreatePermitAsync(HttpClient client, string location)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/permits", Draft(location, "Draft awal"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PermitResponse>()
            ?? throw new InvalidOperationException("API tidak mengembalikan permit.");
    }

    private static async Task<PermitResponse> CreateOpenPermitAsync(
        HttpClient sponsor,
        HttpClient hsse,
        HttpClient areaOwner,
        string location)
    {
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(location, "Flow operasional") with
            {
                ValidFrom = DateTimeOffset.UtcNow.AddMinutes(-5),
                ValidUntil = DateTimeOffset.UtcNow.AddHours(8)
            });
        createResponse.EnsureSuccessStatusCode();
        var created = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var submit = Submit(
            created.Id,
            created.ETag,
            Guid.NewGuid().ToString("N"),
            new SubmitPermitRequest(true, true, true, []));
        using var submitResponse = await sponsor.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var underReview = Required(await submitResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var validation = WorkflowCommand(
            underReview.Id,
            "validations/hsse/endorse",
            underReview.ETag,
            new EndorsePermitValidationRequest("Persyaratan HSSE sesuai."));
        using var validationResponse = await hsse.SendAsync(validation);
        validationResponse.EnsureSuccessStatusCode();
        var awaitingApproval = Required(await validationResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var approval = WorkflowCommand(
            awaitingApproval.Id,
            "approve",
            awaitingApproval.ETag,
            new ApprovePermitRequest("Disetujui PIC pemilik area."));
        using var approvalResponse = await areaOwner.SendAsync(approval);
        approvalResponse.EnsureSuccessStatusCode();
        var approved = Required(await approvalResponse.Content.ReadFromJsonAsync<PermitResponse>());

        using var issue = WorkflowCommand(
            approved.Id,
            "issue",
            approved.ETag,
            new IssuePermitRequest(true, true, true, true, true, true, true, true, false));
        using var issueResponse = await areaOwner.SendAsync(issue);
        issueResponse.EnsureSuccessStatusCode();
        return Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
    }

    private static async Task<PermitResponse> UpdatePermitAsync(
        HttpClient client,
        PermitResponse permit,
        string title)
    {
        using var request = PatchDraft(permit.Id, permit.ETag, Draft(permit.Draft.LocationId, title));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PermitResponse>()
            ?? throw new InvalidOperationException("API tidak mengembalikan permit yang diperbarui.");
    }

    private static HttpRequestMessage PatchDraft(Guid id, string etag, PermitDraftRequest draft)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/permits/{id}/draft")
        {
            Content = JsonContent.Create(draft)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private static HttpRequestMessage Submit(Guid id, string etag, string key, SubmitPermitRequest command)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permits/{id}/submit")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private HttpClient WorkflowClient(string userId, string role, string locations)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", role);
        client.DefaultRequestHeaders.Add("X-Dev-Locations", locations);
        return client;
    }

    private static HttpRequestMessage WorkflowCommand<TRequest>(
        Guid id,
        string command,
        string etag,
        TRequest body,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permits/{id}/{command}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return request;
    }

    private static PermitDraftRequest Draft(string location, string title) => new(
        title,
        "Pekerjaan terencana untuk integration test.",
        location,
        "sponsor.demo",
        "Pelaksana Integration Test",
        "PT Integration Test",
        "ColdWork",
        "Medium",
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddHours(9),
        null,
        null,
        ["Energi tersimpan"],
        ["Isolasi energi"],
        []);

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task<PagedResponse<PermitTaskResponse>> GetTasksAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/tasks");
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<PagedResponse<PermitTaskResponse>>());
    }

    private static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidOperationException("API tidak mengembalikan response yang diharapkan.");
}
