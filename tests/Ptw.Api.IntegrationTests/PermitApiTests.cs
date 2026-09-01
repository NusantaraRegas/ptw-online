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
    public async Task ParallelValidationsGateAreaApprovalAndIssuance()
    {
        const string location = "ORF";
        using var sponsor = factory.CreateClient();
        using var createResponse = await sponsor.PostAsJsonAsync(
            "/api/v1/permits",
            Draft(location, "Flow paralel") with
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
        using var unauthorizedGasValidation = WorkflowCommand(
            underReview.Id,
            "validations/gas-distribution/endorse",
            underReview.ETag,
            new EndorsePermitValidationRequest("Tidak berwenang."));
        using var unauthorizedGasResponse = await hsse.SendAsync(unauthorizedGasValidation);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedGasResponse.StatusCode);

        using var gas = WorkflowClient(
            "gas.validator",
            "GasDistributionValidator",
            "*");
        using var gasValidation = WorkflowCommand(
            underReview.Id,
            "validations/gas-distribution/endorse",
            underReview.ETag,
            new EndorsePermitValidationRequest("Kontrol operasional sesuai."));
        using var gasResponse = await gas.SendAsync(gasValidation);
        gasResponse.EnsureSuccessStatusCode();
        var gasValidated = Required(await gasResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("UNDER_REVIEW", gasValidated.Status);
        Assert.True(gasValidated.Workflow.GasDistribution.Completed);
        Assert.False(gasValidated.Workflow.Hsse.Completed);

        using var hsseValidation = WorkflowCommand(
            gasValidated.Id,
            "validations/hsse/endorse",
            gasValidated.ETag,
            new EndorsePermitValidationRequest("Persyaratan HSSE sesuai."));
        using var hsseResponse = await hsse.SendAsync(hsseValidation);
        hsseResponse.EnsureSuccessStatusCode();
        var awaitingApproval = Required(await hsseResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("AWAITING_APPROVAL", awaitingApproval.Status);
        Assert.True(awaitingApproval.Workflow.Hsse.Completed);
        Assert.True(awaitingApproval.Workflow.GasDistribution.Completed);

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
        using var sameActorIssue = WorkflowCommand(
            approved.Id,
            "issue",
            approved.ETag,
            ready);
        using var sameActorIssueResponse = await areaOwner.SendAsync(sameActorIssue);
        Assert.Equal(HttpStatusCode.Conflict, sameActorIssueResponse.StatusCode);
        Assert.Equal(
            "permit.sod.approver_issuer_conflict",
            await ProblemCodeAsync(sameActorIssueResponse));

        using var issuer = WorkflowClient("issuer.orf", "IssuingAuthority", location);
        using var failedIssue = WorkflowCommand(
            approved.Id,
            "issue",
            approved.ETag,
            ready with { GasTestSatisfied = false });
        using var failedIssueResponse = await issuer.SendAsync(failedIssue);
        Assert.Equal(HttpStatusCode.Conflict, failedIssueResponse.StatusCode);
        Assert.Equal("permit.issue.guards_failed", await ProblemCodeAsync(failedIssueResponse));

        using var issue = WorkflowCommand(
            approved.Id,
            "issue",
            approved.ETag,
            ready);
        using var issueResponse = await issuer.SendAsync(issue);
        issueResponse.EnsureSuccessStatusCode();
        var issued = Required(await issueResponse.Content.ReadFromJsonAsync<PermitResponse>());
        Assert.Equal("OPEN", issued.Status);
        Assert.NotNull(issued.ActiveWorkPeriodId);
    }

    private static async Task<PermitResponse> CreatePermitAsync(HttpClient client, string location)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/permits", Draft(location, "Draft awal"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PermitResponse>()
            ?? throw new InvalidOperationException("API tidak mengembalikan permit.");
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
        TRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/permits/{id}/{command}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
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

    private static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidOperationException("API tidak mengembalikan response yang diharapkan.");
}
