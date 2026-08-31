using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Application;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class UserAuthorizationApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task NonAdministratorCannotReadOrCreateAssignments()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");

        using var list = await client.GetAsync("/api/v1/admin/authorizations");
        using var create = await client.PostAsJsonAsync(
            "/api/v1/admin/authorizations",
            Draft("operator.denied", "PTW_ISSUER", ["permit.issue"]));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(list));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(create));
    }

    [Fact]
    public async Task SubjectCanHoldMultipleRolesWithMakerCheckerAndAtomicEvidence()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"operator.{suffix}";
        using var maker = AdminClient($"admin.maker.{suffix}");
        var issuer = await CreateAsync(maker, Draft(subjectId, "PTW_ISSUER", ["permit.issue"]));
        var tester = await CreateAsync(maker, Draft(subjectId, "GAS_TESTER", ["gas-test.record"]));

        var issuerPending = await SubmitAsync(maker, issuer);
        var testerPending = await SubmitAsync(maker, tester);

        using var selfApproval = Command(
            issuer.Id,
            "approve",
            issuerPending.ETag,
            Guid.NewGuid().ToString("N"));
        using var selfApprovalResponse = await maker.SendAsync(selfApproval);
        Assert.Equal(HttpStatusCode.Conflict, selfApprovalResponse.StatusCode);
        Assert.Equal("authorization.maker_checker_required", await ProblemCodeAsync(selfApprovalResponse));

        using var checker = AdminClient($"admin.checker.{suffix}");
        var issuerApproved = await ApproveAsync(checker, issuerPending);
        var testerApproved = await ApproveAsync(checker, testerPending);

        Assert.Equal("APPROVED", issuerApproved.Status);
        Assert.Equal("APPROVED", testerApproved.Status);
        Assert.Equal(subjectId, issuerApproved.SubjectId);
        Assert.Equal(subjectId, testerApproved.SubjectId);
        Assert.NotEqual(issuerApproved.RoleCode, testerApproved.RoleCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAuthorizationAssignmentResolver>();
        var issuerResolution = await resolver.ResolveAsync(
            subjectId,
            "permit.issue",
            null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var testerResolution = await resolver.ResolveAsync(
            subjectId,
            "gas-test.record",
            null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        Assert.True(issuerResolution.IsResolved);
        Assert.True(testerResolution.IsResolved);
        Assert.Single(issuerResolution.AssignmentIds);
        Assert.Single(testerResolution.AssignmentIds);

        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal(3, await db.UserAuthorizationVersions.CountAsync(
            item => item.UserAuthorizationId == issuer.Id,
            CancellationToken.None));
        Assert.Equal(3, await db.ConfigurationAuditEvents.CountAsync(
            item => item.AggregateId == issuer.Id,
            CancellationToken.None));
        Assert.Equal(3, await db.OutboxMessages.CountAsync(
            item => item.AggregateId == issuer.Id,
            CancellationToken.None));
        Assert.Equal(2, await db.AuthorizationCommandReceipts.CountAsync(
            item => item.UserAuthorizationId == issuer.Id,
            CancellationToken.None));
    }

    [Fact]
    public async Task DelegationCannotBroadenSourceAuthority()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var maker = AdminClient($"admin.delegation-maker.{suffix}");
        using var checker = AdminClient($"admin.delegation-checker.{suffix}");
        var source = await CreateAsync(
            maker,
            Draft($"supervisor.{suffix}", "PTW_ISSUER", ["permit.issue"]));
        var sourcePending = await SubmitAsync(maker, source);
        var sourceApproved = await ApproveAsync(checker, sourcePending);

        var validDelegation = await CreateAsync(
            maker,
            Draft($"valid-delegate.{suffix}", "PTW_ISSUER", ["permit.issue"]) with
            {
                Kind = "DELEGATION",
                SourceAuthorizationId = sourceApproved.Id,
                EffectiveFrom = DateTimeOffset.UtcNow,
                EffectiveUntil = DateTimeOffset.UtcNow.AddDays(7)
            });
        var validPending = await SubmitAsync(maker, validDelegation);
        var validApproved = await ApproveAsync(checker, validPending);
        Assert.Equal("APPROVED", validApproved.Status);

        var delegatedDraft = Draft(
            $"delegate.{suffix}",
            "PTW_ISSUER",
            ["permit.issue", "permit.close"]) with
        {
            Kind = "DELEGATION",
            SourceAuthorizationId = sourceApproved.Id,
            EffectiveFrom = DateTimeOffset.UtcNow,
            EffectiveUntil = DateTimeOffset.UtcNow.AddDays(7)
        };
        var delegated = await CreateAsync(maker, delegatedDraft);
        var delegatedPending = await SubmitAsync(maker, delegated);

        using var approval = Command(
            delegated.Id,
            "approve",
            delegatedPending.ETag,
            Guid.NewGuid().ToString("N"));
        using var approvalResponse = await checker.SendAsync(approval);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, approvalResponse.StatusCode);
        Assert.Equal("authorization.delegation_broadens_scope", await ProblemCodeAsync(approvalResponse));
    }

    [Fact]
    public async Task ResolverFailsSafeForAmbiguousAssignmentWithinSameRole()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var subjectId = $"operator.ambiguous.{suffix}";
        using var maker = AdminClient($"admin.ambiguous-maker.{suffix}");
        using var checker = AdminClient($"admin.ambiguous-checker.{suffix}");

        var first = await CreateAsync(
            maker,
            Draft(subjectId, "PTW_ISSUER", ["permit.issue"]));
        var second = await CreateAsync(
            maker,
            Draft(subjectId, "PTW_ISSUER", ["permit.issue"]));
        await ApproveAsync(checker, await SubmitAsync(maker, first));
        await ApproveAsync(checker, await SubmitAsync(maker, second));

        await using var scope = factory.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAuthorizationAssignmentResolver>();
        var resolution = await resolver.ResolveAsync(
            subjectId,
            "permit.issue",
            null,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(resolution.IsResolved);
        Assert.Equal("authorization.assignment_ambiguous", resolution.Code);
        Assert.Empty(resolution.AssignmentIds);
    }

    [Fact]
    public async Task TransitionRejectsStaleETagAndReplaysOriginalResult()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var maker = AdminClient($"admin.replay-maker.{suffix}");
        var created = await CreateAsync(
            maker,
            Draft($"operator.replay.{suffix}", "AREA_AUTHORITY", ["area.verify"]));
        var submitKey = Guid.NewGuid().ToString("N");

        using var submit = Command(created.Id, "submit", created.ETag, submitKey);
        using var submitResponse = await maker.SendAsync(submit);
        submitResponse.EnsureSuccessStatusCode();
        var pending = Required(await submitResponse.Content.ReadFromJsonAsync<UserAuthorizationResponse>());

        using var stale = Command(
            created.Id,
            "approve",
            created.ETag,
            Guid.NewGuid().ToString("N"));
        using var staleResponse = await AdminClient($"admin.stale-checker.{suffix}").SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("concurrency.conflict", await ProblemCodeAsync(staleResponse));

        using var replay = Command(created.Id, "submit", created.ETag, submitKey);
        using var replayResponse = await maker.SendAsync(replay);
        replayResponse.EnsureSuccessStatusCode();
        var replayed = Required(await replayResponse.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
        Assert.Equal(pending.Version, replayed.Version);
        Assert.Equal(pending.ETag, replayed.ETag);
        Assert.Equal("PENDING_APPROVAL", replayed.Status);
    }

    private HttpClient AdminClient(string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");
        return client;
    }

    private static async Task<UserAuthorizationResponse> CreateAsync(
        HttpClient client,
        UserAuthorizationDraftRequest draft)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/admin/authorizations", draft);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
    }

    private static async Task<UserAuthorizationResponse> SubmitAsync(
        HttpClient client,
        UserAuthorizationResponse assignment)
    {
        using var command = Command(
            assignment.Id,
            "submit",
            assignment.ETag,
            Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(command);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
    }

    private static async Task<UserAuthorizationResponse> ApproveAsync(
        HttpClient client,
        UserAuthorizationResponse assignment)
    {
        using var command = Command(
            assignment.Id,
            "approve",
            assignment.ETag,
            Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(command);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
    }

    private static HttpRequestMessage Command(Guid id, string command, string etag, string key)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/authorizations/{id}/{command}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static UserAuthorizationDraftRequest Draft(
        string subjectId,
        string roleCode,
        IReadOnlyList<string> actions) => new(
        subjectId,
        roleCode,
        actions,
        null,
        false,
        [],
        "DIRECT",
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
