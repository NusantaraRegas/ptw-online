using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ptw.Application;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class OperationalPolicyApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task ReadinessIsAdminOnlyAndDoesNotTreatDraftDecisionsAsAccepted()
    {
        using var administrator = factory.CreateClient();
        using var response = await administrator.GetAsync("/api/v1/admin/policy-readiness");
        response.EnsureSuccessStatusCode();
        var readiness = Required(
            await response.Content.ReadFromJsonAsync<OperationalPolicyReadinessResponse>());

        Assert.False(readiness.EnforcementEnabled);
        Assert.False(readiness.ReadyForActivation);
        Assert.Equal("PREPARATION", readiness.Mode);
        Assert.Contains(
            readiness.Requirements,
            item => item.Code == "decision.opn-001" && !item.Satisfied);
        Assert.Contains(
            readiness.Requirements,
            item => item.Code == "decision.opn-002" && !item.Satisfied);
        Assert.Contains(
            readiness.Requirements,
            item => item.Code == "uat.passing_run" && !item.Satisfied);

        using var sponsor = factory.CreateClient();
        sponsor.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");
        using var denied = await sponsor.GetAsync("/api/v1/admin/policy-readiness");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(denied));

        using var simulationDenied = await sponsor.PostAsJsonAsync(
            "/api/v1/admin/policy-simulations",
            new PolicySimulationRequest("subject", "action", "location", [], null));
        Assert.Equal(HttpStatusCode.Forbidden, simulationDenied.StatusCode);
        Assert.Equal("authorization.denied", await ProblemCodeAsync(simulationDenied));
    }

    [Fact]
    public async Task UatSuiteVersionsRunsIdempotentlyAndStoresAtomicEvidence()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var locationCode = $"UAT-{suffix}";
        var subjectId = $"operator.uat.{suffix}";
        var actionCode = $"permit.uat.{suffix}";
        var competencyCode = $"competency.uat.{suffix}";
        var policyVersion = $"policy-uat-{suffix}";
        using var maker = AdminClient(factory, $"uat.maker.{suffix}");
        using var checker = AdminClient(factory, $"uat.checker.{suffix}");

        var location = await ApproveLocationAsync(
            checker,
            await SubmitLocationAsync(maker, await CreateLocationAsync(maker, locationCode)));
        await ApproveAuthorizationAsync(
            checker,
            await SubmitAuthorizationAsync(
                maker,
                await CreateAuthorizationAsync(
                    maker,
                    new UserAuthorizationDraftRequest(
                        subjectId,
                        "UAT_ROLE",
                        [actionCode],
                        location.Id,
                        false,
                        [competencyCode],
                        "DIRECT",
                        null,
                        DateTimeOffset.UtcNow.AddHours(-1),
                        DateTimeOffset.UtcNow.AddDays(1)))));

        var draft = UatDraft(
            $"SUITE-{suffix}",
            policyVersion,
            subjectId,
            actionCode,
            locationCode,
            competencyCode);
        using var administrator = AdminClient(factory, $"uat.admin.{suffix}");
        var createKey = Guid.NewGuid().ToString("N");
        using var create = UatCommand(HttpMethod.Post, "/api/v1/admin/policy-uat-suites", createKey, draft);
        using var createResponse = await administrator.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var suite = Required(await createResponse.Content.ReadFromJsonAsync<PolicyUatSuiteResponse>());
        Assert.Equal(1, suite.Version);
        Assert.Equal(64, suite.ContentHash.Length);

        using var replay = UatCommand(HttpMethod.Post, "/api/v1/admin/policy-uat-suites", createKey, draft);
        using var replayResponse = await administrator.SendAsync(replay);
        var replayed = Required(await replayResponse.Content.ReadFromJsonAsync<PolicyUatSuiteResponse>());
        Assert.Equal(suite.Id, replayed.Id);

        using var mismatch = UatCommand(
            HttpMethod.Post,
            "/api/v1/admin/policy-uat-suites",
            createKey,
            draft with { Name = "Payload berbeda" });
        using var mismatchResponse = await administrator.SendAsync(mismatch);
        Assert.Equal(HttpStatusCode.Conflict, mismatchResponse.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(mismatchResponse));

        using var secondVersion = UatCommand(
            HttpMethod.Post,
            "/api/v1/admin/policy-uat-suites",
            Guid.NewGuid().ToString("N"),
            draft);
        using var secondVersionResponse = await administrator.SendAsync(secondVersion);
        var suiteV2 = Required(
            await secondVersionResponse.Content.ReadFromJsonAsync<PolicyUatSuiteResponse>());
        Assert.Equal(2, suiteV2.Version);
        Assert.NotEqual(suite.Id, suiteV2.Id);

        var runKey = Guid.NewGuid().ToString("N");
        using var run = UatCommand(
            HttpMethod.Post,
            $"/api/v1/admin/policy-uat-suites/{suite.Id}/runs",
            runKey);
        using var runResponse = await administrator.SendAsync(run);
        runResponse.EnsureSuccessStatusCode();
        var report = Required(await runResponse.Content.ReadFromJsonAsync<PolicyUatRunResponse>());
        Assert.True(report.Passed);
        Assert.Equal(2, report.Coverage.ScenarioCount);
        Assert.Equal(2, report.Coverage.MatchedCount);
        Assert.Equal(1, report.Coverage.ExpectedAllowCount);
        Assert.Equal(1, report.Coverage.ExpectedDenyCount);
        Assert.Equal(64, report.ReportHash.Length);
        Assert.All(report.Results, result => Assert.True(result.Matched));

        using var runReplay = UatCommand(
            HttpMethod.Post,
            $"/api/v1/admin/policy-uat-suites/{suite.Id}/runs",
            runKey);
        using var runReplayResponse = await administrator.SendAsync(runReplay);
        var replayedReport = Required(
            await runReplayResponse.Content.ReadFromJsonAsync<PolicyUatRunResponse>());
        Assert.Equal(report.Id, replayedReport.Id);
        Assert.Equal(report.ReportHash, replayedReport.ReportHash);

        using var runKeyMismatch = UatCommand(
            HttpMethod.Post,
            $"/api/v1/admin/policy-uat-suites/{suiteV2.Id}/runs",
            runKey);
        using var runKeyMismatchResponse = await administrator.SendAsync(runKeyMismatch);
        Assert.Equal(HttpStatusCode.Conflict, runKeyMismatchResponse.StatusCode);
        Assert.Equal("idempotency.payload_mismatch", await ProblemCodeAsync(runKeyMismatchResponse));

        var invalidDraft = draft with
        {
            Scenarios = [draft.Scenarios[0], draft.Scenarios[0]]
        };
        using var invalid = UatCommand(
            HttpMethod.Post,
            "/api/v1/admin/policy-uat-suites",
            Guid.NewGuid().ToString("N"),
            invalidDraft);
        using var invalidResponse = await administrator.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResponse.StatusCode);
        Assert.Equal("policy.uat_duplicate_case", await ProblemCodeAsync(invalidResponse));

        using var sponsor = factory.CreateClient();
        sponsor.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");
        using var forbidden = await sponsor.GetAsync("/api/v1/admin/policy-uat-suites");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        Assert.Equal(2, await db.PolicyUatSuites.CountAsync(x => x.SuiteKey == draft.SuiteKey));
        Assert.Single(await db.PolicyUatRuns.Where(x => x.PolicyUatSuiteId == suite.Id).ToListAsync());
        Assert.Equal(
            3,
            await db.PolicyUatCommandReceipts.CountAsync(
                x => x.ActorId == $"uat.admin.{suffix}"));
        Assert.Equal(
            3,
            await db.ConfigurationAuditEvents.CountAsync(
                x => (x.AggregateId == suite.Id || x.AggregateId == suiteV2.Id)
                    && (x.EventType == "PolicyUatSuiteCreated" || x.EventType == "PolicyUatRunCompleted")));
        Assert.Equal(
            3,
            await db.OutboxMessages.CountAsync(
                x => (x.AggregateId == suite.Id || x.AggregateId == suiteV2.Id)
                    && (x.EventType == "PolicyUatSuiteCreated" || x.EventType == "PolicyUatRunCompleted")));
    }

    [Fact]
    public async Task SimulationExplainsAllowAndDenyWithoutMutatingData()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var locationCode = $"SIMULATION-{suffix}";
        var subjectId = $"operator.simulation.{suffix}";
        var actionCode = $"permit.simulate.{suffix}";
        var competencyCode = $"competency.simulation.{suffix}";
        using var maker = AdminClient(factory, $"simulation.maker.{suffix}");
        using var checker = AdminClient(factory, $"simulation.checker.{suffix}");

        var location = await ApproveLocationAsync(
            checker,
            await SubmitLocationAsync(maker, await CreateLocationAsync(maker, locationCode)));
        var assignment = await CreateAuthorizationAsync(
            maker,
            new UserAuthorizationDraftRequest(
                subjectId,
                "SIMULATION_ROLE",
                [actionCode],
                location.Id,
                false,
                [competencyCode],
                "DIRECT",
                null,
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddDays(1)));
        await ApproveAuthorizationAsync(
            checker,
            await SubmitAuthorizationAsync(maker, assignment));

        var before = await DatabaseCountsAsync(factory.Services);
        using var administrator = AdminClient(factory, $"simulation.admin.{suffix}");
        using var allowedResponse = await administrator.PostAsJsonAsync(
            "/api/v1/admin/policy-simulations",
            new PolicySimulationRequest(
                subjectId,
                actionCode,
                locationCode,
                [competencyCode],
                DateTimeOffset.UtcNow));
        allowedResponse.EnsureSuccessStatusCode();
        var allowed = Required(await allowedResponse.Content.ReadFromJsonAsync<PolicySimulationResponse>());

        Assert.True(allowed.Allowed);
        Assert.Equal("ALLOW", allowed.Outcome);
        Assert.Equal("authorization.simulation_allowed", allowed.Code);
        Assert.False(allowed.IsAuthoritative);
        Assert.Equal(location.Id, allowed.Location?.Id);
        Assert.Single(allowed.Assignments);
        Assert.Equal("SIMULATION_ROLE", allowed.Assignments[0].RoleCode);
        Assert.Contains(competencyCode, allowed.RequiredCompetencyCodes);
        Assert.Empty(allowed.MissingCompetencyCodes);
        Assert.All(allowed.Checks, check => Assert.True(check.Passed));

        using var deniedResponse = await administrator.PostAsJsonAsync(
            "/api/v1/admin/policy-simulations",
            new PolicySimulationRequest(subjectId, actionCode, locationCode, [], DateTimeOffset.UtcNow));
        deniedResponse.EnsureSuccessStatusCode();
        var denied = Required(await deniedResponse.Content.ReadFromJsonAsync<PolicySimulationResponse>());

        Assert.False(denied.Allowed);
        Assert.Equal("DENY", denied.Outcome);
        Assert.Equal("authorization.competency_missing", denied.Code);
        Assert.False(denied.IsAuthoritative);
        Assert.Contains(competencyCode, denied.MissingCompetencyCodes);
        Assert.Contains(denied.Checks, check => check.Code == "authorization.competency" && !check.Passed);

        using var missingAssignmentResponse = await administrator.PostAsJsonAsync(
            "/api/v1/admin/policy-simulations",
            new PolicySimulationRequest(
                $"{subjectId}.missing",
                actionCode,
                locationCode,
                [competencyCode],
                DateTimeOffset.UtcNow));
        missingAssignmentResponse.EnsureSuccessStatusCode();
        var missingAssignment = Required(
            await missingAssignmentResponse.Content.ReadFromJsonAsync<PolicySimulationResponse>());
        Assert.False(missingAssignment.Allowed);
        Assert.Equal("authorization.assignment_missing", missingAssignment.Code);

        using var invalidResponse = await administrator.PostAsJsonAsync(
            "/api/v1/admin/policy-simulations",
            new PolicySimulationRequest(string.Empty, actionCode, locationCode, [], null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResponse.StatusCode);
        Assert.Equal("policy.simulation_invalid", await ProblemCodeAsync(invalidResponse));

        var after = await DatabaseCountsAsync(factory.Services);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task EnforcedPolicyUsesEffectiveLocationAssignmentCompetencyAndAtomicAudit()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var locationCode = $"POLICY-{suffix}";
        var subjectId = $"sponsor.policy.{suffix}";
        var actionCode = $"permit.create.{suffix}";
        var competencyCode = $"competency.{suffix}";
        using var maker = AdminClient(factory, $"policy.maker.{suffix}");
        using var checker = AdminClient(factory, $"policy.checker.{suffix}");

        var location = await CreateLocationAsync(maker, locationCode);
        location = await SubmitLocationAsync(maker, location);
        location = await ApproveLocationAsync(checker, location);

        var assignment = await CreateAuthorizationAsync(
            maker,
            new UserAuthorizationDraftRequest(
                subjectId,
                "SPONSOR_POLICY_TEST",
                [actionCode],
                location.Id,
                false,
                [competencyCode],
                "DIRECT",
                null,
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddDays(1)));
        assignment = await SubmitAuthorizationAsync(maker, assignment);
        await ApproveAuthorizationAsync(checker, assignment);

        using var uatAdministrator = AdminClient(factory, $"policy.uat.admin.{suffix}");
        await CreateAndRunPassingUatAsync(
            uatAdministrator,
            UatDraft(
                $"POLICY-ACTIVATION-{suffix}",
                "integration-test-v1",
                subjectId,
                actionCode,
                locationCode,
                competencyCode));

        using (var mismatchedVersionFactory = PolicyFactory(
                   factory,
                   PolicyConfiguration(actionCode, "INTEGRATION-TEST-V1")))
        using (var mismatchedVersionAdmin = mismatchedVersionFactory.CreateClient())
        using (var mismatchedVersionResponse = await mismatchedVersionAdmin.GetAsync(
                   "/api/v1/admin/policy-readiness"))
        {
            mismatchedVersionResponse.EnsureSuccessStatusCode();
            var mismatchedReadiness = Required(
                await mismatchedVersionResponse.Content.ReadFromJsonAsync<OperationalPolicyReadinessResponse>());
            Assert.False(mismatchedReadiness.ReadyForActivation);
            Assert.Contains(
                mismatchedReadiness.Requirements,
                item => item.Code == "uat.passing_run" && !item.Satisfied);
        }

        using var policyFactory = PolicyFactory(factory, PolicyConfiguration(actionCode));
        using var authorized = policyFactory.CreateClient();
        authorized.DefaultRequestHeaders.Add("X-Dev-User", subjectId);
        authorized.DefaultRequestHeaders.Add("X-Dev-Name", subjectId);
        authorized.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");
        authorized.DefaultRequestHeaders.Add("X-Dev-Locations", "*");
        authorized.DefaultRequestHeaders.Add("X-Dev-Competencies", competencyCode);

        using var readinessResponse = await authorized.GetAsync("/api/v1/admin/policy-readiness");
        Assert.Equal(HttpStatusCode.Forbidden, readinessResponse.StatusCode);

        using var admin = policyFactory.CreateClient();
        using var adminReadinessResponse = await admin.GetAsync("/api/v1/admin/policy-readiness");
        adminReadinessResponse.EnsureSuccessStatusCode();
        var readiness = Required(
            await adminReadinessResponse.Content.ReadFromJsonAsync<OperationalPolicyReadinessResponse>());
        Assert.True(readiness.EnforcementEnabled);
        Assert.True(readiness.ReadyForActivation);
        Assert.Equal("MASTER_AUTHORIZATION", readiness.Mode);
        Assert.Contains(
            readiness.Requirements,
            item => item.Code == "uat.passing_run" && item.Satisfied);

        using var createResponse = await authorized.PostAsJsonAsync(
            "/api/v1/permits",
            PermitDraft(locationCode, subjectId));
        createResponse.EnsureSuccessStatusCode();
        var created = Required(await createResponse.Content.ReadFromJsonAsync<PermitResponse>());

        await using var scope = policyFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var evidence = await db.AuditEvents.AsNoTracking().SingleAsync(
            item => item.PermitId == created.Id && item.EventType == "PermitAuthorizationEvaluated",
            CancellationToken.None);
        using var evidenceJson = JsonDocument.Parse(evidence.PayloadJson);
        Assert.Equal(actionCode, evidenceJson.RootElement.GetProperty("actionCode").GetString());
        Assert.Equal(location.Id, evidenceJson.RootElement.GetProperty("locationMasterId").GetGuid());

        using var missingCompetency = policyFactory.CreateClient();
        missingCompetency.DefaultRequestHeaders.Add("X-Dev-User", subjectId);
        missingCompetency.DefaultRequestHeaders.Add("X-Dev-Roles", "Sponsor");
        missingCompetency.DefaultRequestHeaders.Add("X-Dev-Locations", "*");
        using var competencyDenied = await missingCompetency.PostAsJsonAsync(
            "/api/v1/permits",
            PermitDraft(locationCode, subjectId));
        Assert.Equal(HttpStatusCode.Forbidden, competencyDenied.StatusCode);
        Assert.Equal("authorization.competency_missing", await ProblemCodeAsync(competencyDenied));
    }

    [Fact]
    public async Task EnforcementEnabledBeforeDecisionsAreConfiguredFailsClosed()
    {
        using var policyFactory = PolicyFactory(
            factory,
            new OperationalPolicySettings { EnforceMasterAuthorization = true });
        using var client = policyFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/permits",
            PermitDraft("UNCONFIGURED-LOCATION", "sponsor.demo"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("policy.activation_not_ready", await ProblemCodeAsync(response));
    }

    private static OperationalPolicySettings PolicyConfiguration(
        string actionCode,
        string policyVersion = "integration-test-v1") => new()
        {
            EnforceMasterAuthorization = true,
            PolicyVersion = policyVersion,
            AcceptedDecisionReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OPN-001"] = "TEST-OPN-001-ACCEPTED",
                ["OPN-002"] = "TEST-OPN-002-ACCEPTED"
            },
            PermitActionCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PermitPolicyOperations.CreateDraft] = actionCode,
                [PermitPolicyOperations.UpdateDraft] = $"{actionCode}.update",
                [PermitPolicyOperations.Submit] = $"{actionCode}.submit",
                [PermitPolicyOperations.ValidateHsse] = $"{actionCode}.validate-hsse",
                [PermitPolicyOperations.ValidateGasDistribution] = $"{actionCode}.validate-gas",
                [PermitPolicyOperations.Approve] = $"{actionCode}.approve",
                [PermitPolicyOperations.Issue] = $"{actionCode}.issue"
            }
        };

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> PolicyFactory(
        PtwApiFactory factory,
        OperationalPolicySettings settings) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<OperationalPolicySettings>();
            services.AddSingleton(settings);
        }));

    private static HttpClient AdminClient(PtwApiFactory factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Administrator");
        return client;
    }

    private static async Task<LocationMasterResponse> CreateLocationAsync(HttpClient client, string code)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/locations",
            new LocationDraftRequest(
                code,
                $"Lokasi {code}",
                null,
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddDays(2)));
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<LocationMasterResponse>());
    }

    private static Task<LocationMasterResponse> SubmitLocationAsync(
        HttpClient client,
        LocationMasterResponse location) =>
        LocationCommandAsync(client, location, "submit");

    private static Task<LocationMasterResponse> ApproveLocationAsync(
        HttpClient client,
        LocationMasterResponse location) =>
        LocationCommandAsync(client, location, "approve");

    private static async Task<LocationMasterResponse> LocationCommandAsync(
        HttpClient client,
        LocationMasterResponse location,
        string command)
    {
        using var request = Command(
            $"/api/v1/admin/locations/{location.Id}/{command}",
            location.ETag);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<LocationMasterResponse>());
    }

    private static async Task<UserAuthorizationResponse> CreateAuthorizationAsync(
        HttpClient client,
        UserAuthorizationDraftRequest draft)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/admin/authorizations", draft);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
    }

    private static Task<UserAuthorizationResponse> SubmitAuthorizationAsync(
        HttpClient client,
        UserAuthorizationResponse assignment) =>
        AuthorizationCommandAsync(client, assignment, "submit");

    private static Task<UserAuthorizationResponse> ApproveAuthorizationAsync(
        HttpClient client,
        UserAuthorizationResponse assignment) =>
        AuthorizationCommandAsync(client, assignment, "approve");

    private static async Task<UserAuthorizationResponse> AuthorizationCommandAsync(
        HttpClient client,
        UserAuthorizationResponse assignment,
        string command)
    {
        using var request = Command(
            $"/api/v1/admin/authorizations/{assignment.Id}/{command}",
            assignment.ETag);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return Required(await response.Content.ReadFromJsonAsync<UserAuthorizationResponse>());
    }

    private static HttpRequestMessage Command(string path, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private static PermitDraftRequest PermitDraft(string locationCode, string sponsorId) => new(
        "Policy activation test",
        "Memastikan assignment dan kompetensi diverifikasi server.",
        locationCode,
        sponsorId,
        "Pelaksana Policy Test",
        "PT Policy Test",
        "ColdWork",
        "Medium",
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddHours(8),
        null,
        null,
        ["Energi tersimpan"],
        ["Isolasi energi"],
        []);

    private static PolicyUatSuiteDraftRequest UatDraft(
        string suiteKey,
        string policyVersion,
        string subjectId,
        string actionCode,
        string locationCode,
        string competencyCode) => new(
        suiteKey,
        $"UAT {suiteKey}",
        policyVersion,
        [
            new PolicyUatScenarioRequest(
                "ALLOW-WITH-COMPETENCY",
                "Assignment dan kompetensi lengkap harus diizinkan.",
                subjectId,
                actionCode,
                locationCode,
                [competencyCode],
                DateTimeOffset.UtcNow,
                "ALLOW",
                "authorization.simulation_allowed"),
            new PolicyUatScenarioRequest(
                "DENY-MISSING-COMPETENCY",
                "Kompetensi yang hilang harus ditolak.",
                subjectId,
                actionCode,
                locationCode,
                [],
                DateTimeOffset.UtcNow,
                "DENY",
                "authorization.competency_missing")
        ]);

    private static async Task CreateAndRunPassingUatAsync(
        HttpClient client,
        PolicyUatSuiteDraftRequest draft)
    {
        using var create = UatCommand(
            HttpMethod.Post,
            "/api/v1/admin/policy-uat-suites",
            Guid.NewGuid().ToString("N"),
            draft);
        using var createResponse = await client.SendAsync(create);
        createResponse.EnsureSuccessStatusCode();
        var suite = Required(
            await createResponse.Content.ReadFromJsonAsync<PolicyUatSuiteResponse>());
        using var run = UatCommand(
            HttpMethod.Post,
            $"/api/v1/admin/policy-uat-suites/{suite.Id}/runs",
            Guid.NewGuid().ToString("N"));
        using var runResponse = await client.SendAsync(run);
        runResponse.EnsureSuccessStatusCode();
        var report = Required(await runResponse.Content.ReadFromJsonAsync<PolicyUatRunResponse>());
        Assert.True(report.Passed);
    }

    private static HttpRequestMessage UatCommand(
        HttpMethod method,
        string path,
        string idempotencyKey,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static T Required<T>(T? value) where T : class =>
        value ?? throw new InvalidOperationException("API tidak mengembalikan response yang diharapkan.");

    private static async Task<DatabaseCounts> DatabaseCountsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        return new DatabaseCounts(
            await db.Permits.CountAsync(),
            await db.AuditEvents.CountAsync(),
            await db.OutboxMessages.CountAsync(),
            await db.ConfigurationAuditEvents.CountAsync(),
            await db.LocationMasterVersions.CountAsync(),
            await db.UserAuthorizationVersions.CountAsync(),
            await db.LocationCommandReceipts.CountAsync(),
            await db.AuthorizationCommandReceipts.CountAsync(),
            await db.PolicyUatSuites.CountAsync(),
            await db.PolicyUatRuns.CountAsync(),
            await db.PolicyUatCommandReceipts.CountAsync());
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private sealed record DatabaseCounts(
        int Permits,
        int PermitAuditEvents,
        int OutboxMessages,
        int ConfigurationAuditEvents,
        int LocationVersions,
        int AuthorizationVersions,
        int LocationReceipts,
        int AuthorizationReceipts,
        int PolicyUatSuites,
        int PolicyUatRuns,
        int PolicyUatReceipts);
}
