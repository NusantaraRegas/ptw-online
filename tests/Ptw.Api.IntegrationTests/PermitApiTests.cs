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
        using var response = await scopedClient.GetAsync($"/api/v1/permits/{created.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(response));
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
        Assert.Equal("SUBMITTED", submitted.Status);

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

    private static async Task<PermitResponse> CreatePermitAsync(HttpClient client, string location)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/permits", Draft(location, "Draft awal"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PermitResponse>()
            ?? throw new InvalidOperationException("API tidak mengembalikan permit.");
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
}
