using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class LocationMasterApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task NonAdministratorCannotReadOrCreateLocationMaster()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");

        using var list = await client.GetAsync("/api/v1/admin/locations");
        using var create = await client.PostAsJsonAsync("/api/v1/admin/locations", Draft("DENIED-AREA"));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(list));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(create));
    }

    [Fact]
    public async Task LocationLifecycleEnforcesMakerCheckerAndStoresAtomicEvidence()
    {
        using var maker = AdminClient("admin.maker");
        var created = await CreateAsync(maker, Draft("AREA-MAKER-CHECKER"));
        Assert.Equal("DRAFT", created.Status);
        Assert.Equal(1, created.Version);

        var submitKey = Guid.NewGuid().ToString("N");
        using var submit = Command(created.Id, "submit", created.ETag, submitKey);
        using var submitResponse = await maker.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var pending = Required(await submitResponse.Content.ReadFromJsonAsync<LocationMasterResponse>());
        Assert.Equal("PENDING_APPROVAL", pending.Status);
        Assert.Equal(2, pending.Version);

        using var submitReplay = Command(created.Id, "submit", created.ETag, submitKey);
        using var submitReplayResponse = await maker.SendAsync(submitReplay);
        submitReplayResponse.EnsureSuccessStatusCode();
        var replayed = Required(await submitReplayResponse.Content.ReadFromJsonAsync<LocationMasterResponse>());
        Assert.Equal(pending.Version, replayed.Version);
        Assert.Equal(pending.ETag, replayed.ETag);

        using var makerApproval = Command(
            created.Id,
            "approve",
            pending.ETag,
            Guid.NewGuid().ToString("N"));
        using var makerApprovalResponse = await maker.SendAsync(makerApproval);
        Assert.Equal(HttpStatusCode.Conflict, makerApprovalResponse.StatusCode);
        Assert.Equal("location.maker_checker_required", await ProblemCodeAsync(makerApprovalResponse));

        using var checker = AdminClient("admin.checker");
        var approveKey = Guid.NewGuid().ToString("N");
        using var approval = Command(created.Id, "approve", pending.ETag, approveKey);
        using var approvalResponse = await checker.SendAsync(approval);
        approvalResponse.EnsureSuccessStatusCode();
        var approved = Required(await approvalResponse.Content.ReadFromJsonAsync<LocationMasterResponse>());
        Assert.Equal("APPROVED", approved.Status);
        Assert.True(approved.IsEffective);
        Assert.Equal("admin.checker", approved.CheckerId);
        Assert.Equal(3, approved.Version);

        using var lateSubmitReplay = Command(created.Id, "submit", created.ETag, submitKey);
        using var lateSubmitReplayResponse = await maker.SendAsync(lateSubmitReplay);
        lateSubmitReplayResponse.EnsureSuccessStatusCode();
        var lateReplay = Required(await lateSubmitReplayResponse.Content.ReadFromJsonAsync<LocationMasterResponse>());
        Assert.Equal("PENDING_APPROVAL", lateReplay.Status);
        Assert.Equal(2, lateReplay.Version);
        Assert.Equal(pending.ETag, lateReplay.ETag);

        using var immutableUpdate = PatchDraft(created.Id, approved.ETag, Draft("AREA-CHANGED"));
        using var immutableResponse = await maker.SendAsync(immutableUpdate);
        Assert.Equal(HttpStatusCode.Conflict, immutableResponse.StatusCode);
        Assert.Equal("location.invalid_transition", await ProblemCodeAsync(immutableResponse));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal(3, await db.LocationMasterVersions.CountAsync(x => x.LocationMasterId == created.Id));
        Assert.Equal(3, await db.ConfigurationAuditEvents.CountAsync(x => x.AggregateId == created.Id));
        Assert.Equal(3, await db.OutboxMessages.CountAsync(x => x.AggregateId == created.Id));
        Assert.Equal(2, await db.LocationCommandReceipts.CountAsync(x => x.LocationMasterId == created.Id));
    }

    [Fact]
    public async Task LocationDraftRejectsStaleETagAndHierarchyCycle()
    {
        using var admin = AdminClient("admin.hierarchy");
        var parent = await CreateAsync(admin, Draft("AREA-PARENT"));
        var child = await CreateAsync(admin, Draft("AREA-CHILD") with { ParentId = parent.Id });

        using var validUpdate = PatchDraft(
            parent.Id,
            parent.ETag,
            Draft("AREA-PARENT", "Parent diperbarui"));
        using var validUpdateResponse = await admin.SendAsync(validUpdate);
        validUpdateResponse.EnsureSuccessStatusCode();
        var updatedParent = Required(await validUpdateResponse.Content.ReadFromJsonAsync<LocationMasterResponse>());

        using var staleUpdate = PatchDraft(parent.Id, parent.ETag, Draft("AREA-PARENT", "Versi stale"));
        using var staleResponse = await admin.SendAsync(staleUpdate);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("concurrency.conflict", await ProblemCodeAsync(staleResponse));

        using var cycleUpdate = PatchDraft(
            parent.Id,
            updatedParent.ETag,
            Draft("AREA-PARENT", "Parent diperbarui") with { ParentId = child.Id });
        using var cycleResponse = await admin.SendAsync(cycleUpdate);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycleResponse.StatusCode);
        Assert.Equal("location.hierarchy_cycle", await ProblemCodeAsync(cycleResponse));
    }

    [Fact]
    public async Task ReturnForChangesRejectsIdempotencyKeyWithDifferentReason()
    {
        using var maker = AdminClient("admin.return-maker");
        var created = await CreateAsync(maker, Draft("AREA-RETURN"));
        using var submit = Command(created.Id, "submit", created.ETag, Guid.NewGuid().ToString("N"));
        using var submitResponse = await maker.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var pending = Required(await submitResponse.Content.ReadFromJsonAsync<LocationMasterResponse>());

        using var checker = AdminClient("admin.return-checker");
        var key = Guid.NewGuid().ToString("N");
        using var returned = ReturnForChanges(created.Id, pending.ETag, key, "Perjelas nama lokasi");
        using var returnedResponse = await checker.SendAsync(returned);
        returnedResponse.EnsureSuccessStatusCode();

        using var mismatch = ReturnForChanges(created.Id, pending.ETag, key, "Alasan yang berbeda");
        using var mismatchResponse = await checker.SendAsync(mismatch);
        Assert.Equal(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(mismatchResponse));
    }

    private HttpClient AdminClient(string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");
        return client;
    }

    private static async Task<LocationMasterResponse> CreateAsync(HttpClient client, LocationDraftRequest draft)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/admin/locations", draft);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<LocationMasterResponse>());
    }

    private static HttpRequestMessage PatchDraft(Guid id, string etag, LocationDraftRequest draft)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admin/locations/{id}/draft")
        {
            Content = JsonContent.Create(draft)
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private static HttpRequestMessage Command(Guid id, string command, string etag, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/locations/{id}/{command}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static HttpRequestMessage ReturnForChanges(Guid id, string etag, string key, string reason)
    {
        var request = Command(id, "return-for-changes", etag, key);
        request.Content = JsonContent.Create(new ReturnLocationForChangesRequest(reason));
        return request;
    }

    private static LocationDraftRequest Draft(string code, string? name = null) => new(
        code,
        name ?? $"Lokasi {code}",
        null,
        DateTimeOffset.UtcNow.AddHours(-1),
        DateTimeOffset.UtcNow.AddDays(30));

    private static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidOperationException("API tidak mengembalikan response yang diharapkan.");

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }
}
