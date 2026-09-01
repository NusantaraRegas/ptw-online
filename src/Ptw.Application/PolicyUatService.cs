using System.Security.Cryptography;
using System.Text.Json;
using Ptw.Contracts;

namespace Ptw.Application;

public sealed class PolicyUatService(
    IPolicyUatStore store,
    PolicySimulationService simulationService,
    IActorContext actorContext,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PagedResponse<PolicyUatSuiteResponse>> ListSuitesAsync(
        CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        var suites = (await store.ListSuitesAsync(cancellationToken)).Select(ToResponse).ToArray();
        return new PagedResponse<PolicyUatSuiteResponse>(suites, suites.Length);
    }

    public async Task<PolicyUatSuiteResponse> GetSuiteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        return ToResponse(await GetSuiteEntryAsync(id, cancellationToken));
    }

    public async Task<PagedResponse<PolicyUatRunResponse>> ListRunsAsync(
        Guid suiteId,
        CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        _ = await GetSuiteEntryAsync(suiteId, cancellationToken);
        var runs = (await store.ListRunsAsync(suiteId, cancellationToken)).Select(ToResponse).ToArray();
        return new PagedResponse<PolicyUatRunResponse>(runs, runs.Length);
    }

    public async Task<PolicyUatSuiteResponse> CreateSuiteAsync(
        PolicyUatSuiteDraftRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        EnsureIdempotencyKey(idempotencyKey);
        var draft = Normalize(request);
        var command = new PolicyUatCommandContext(
            actor.Id,
            "CreatePolicyUatSuite",
            idempotencyKey,
            Hash(draft));
        var prior = await store.FindSuiteCommandResultAsync(command, cancellationToken);
        if (prior is not null)
        {
            return ToResponse(prior);
        }

        return ToResponse(await store.AddSuiteAsync(
            draft,
            actor,
            clock.UtcNow,
            correlationId,
            command,
            cancellationToken));
    }

    public async Task<PolicyUatRunResponse> RunSuiteAsync(
        Guid suiteId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        EnsureIdempotencyKey(idempotencyKey);
        var suite = await GetSuiteEntryAsync(suiteId, cancellationToken);
        var command = new PolicyUatCommandContext(
            actor.Id,
            "RunPolicyUatSuite",
            idempotencyKey,
            Hash(new { SuiteId = suite.Id, suite.ContentHash }));
        var prior = await store.FindRunCommandResultAsync(command, cancellationToken);
        if (prior is not null)
        {
            return ToResponse(prior);
        }

        var results = new List<PolicyUatScenarioResultResponse>(suite.Scenarios.Count);
        foreach (var scenario in suite.Scenarios)
        {
            var actual = await simulationService.SimulateAsync(
                new PolicySimulationRequest(
                    scenario.SubjectId,
                    scenario.ActionCode,
                    scenario.LocationCode,
                    scenario.CompetencyCodes,
                    scenario.EvaluatedAt),
                cancellationToken);
            var expectedCodeMatches = string.IsNullOrWhiteSpace(scenario.ExpectedCode)
                || string.Equals(scenario.ExpectedCode, actual.Code, StringComparison.OrdinalIgnoreCase);
            var matched = string.Equals(
                    scenario.ExpectedOutcome,
                    actual.Outcome,
                    StringComparison.OrdinalIgnoreCase)
                && expectedCodeMatches;
            results.Add(new PolicyUatScenarioResultResponse(
                scenario.CaseCode,
                scenario.ExpectedOutcome,
                scenario.ExpectedCode,
                actual.Outcome,
                actual.Code,
                matched,
                actual));
        }

        var coverage = Coverage(suite.Scenarios, results);
        return ToResponse(await store.AddRunAsync(
            suite,
            results.All(item => item.Matched),
            coverage,
            results,
            actor,
            clock.UtcNow,
            correlationId,
            command,
            cancellationToken));
    }

    private static PolicyUatCoverageResponse Coverage(
        IReadOnlyList<PolicyUatScenarioRequest> scenarios,
        IReadOnlyList<PolicyUatScenarioResultResponse> results) => new(
        scenarios.Count,
        scenarios.Count(item => item.ExpectedOutcome == "ALLOW"),
        scenarios.Count(item => item.ExpectedOutcome == "DENY"),
        results.Count(item => item.ActualOutcome == "ALLOW"),
        results.Count(item => item.ActualOutcome == "DENY"),
        results.Count(item => item.Matched),
        scenarios.Select(item => item.SubjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        scenarios.Select(item => item.ActionCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        scenarios.Select(item => item.LocationCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        results.SelectMany(item => item.Actual.Assignments).Select(item => item.RoleCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        scenarios.SelectMany(item => item.CompetencyCodes).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        scenarios.Count(item => item.EvaluatedAt is not null));

    private static PolicyUatSuiteDraftRequest Normalize(PolicyUatSuiteDraftRequest request)
    {
        var suiteKey = RequiredValue(request.SuiteKey, "suiteKey", 100);
        var name = RequiredValue(request.Name, "name", 200);
        var policyVersion = RequiredValue(request.PolicyVersion, "policyVersion", 100);
        var requestedScenarios = request.Scenarios ?? [];
        if (requestedScenarios.Count is < 1 or > 200)
        {
            throw new InvalidRequestException(
                "policy.uat_invalid",
                "Paket UAT wajib memiliki 1 sampai 200 skenario.");
        }

        var scenarios = requestedScenarios.Select(NormalizeScenario).ToArray();
        var duplicate = scenarios.GroupBy(item => item.CaseCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidRequestException(
                "policy.uat_duplicate_case",
                $"Case code '{duplicate.Key}' muncul lebih dari satu kali.");
        }

        return new PolicyUatSuiteDraftRequest(suiteKey, name, policyVersion, scenarios);
    }

    private static PolicyUatScenarioRequest NormalizeScenario(PolicyUatScenarioRequest scenario)
    {
        var outcome = RequiredValue(scenario.ExpectedOutcome, "expectedOutcome", 10).ToUpperInvariant();
        if (outcome is not ("ALLOW" or "DENY"))
        {
            throw new InvalidRequestException(
                "policy.uat_invalid_outcome",
                "Expected outcome harus ALLOW atau DENY.");
        }

        var competencyCodes = scenario.CompetencyCodes ?? [];
        if (competencyCodes.Count > 100)
        {
            throw new InvalidRequestException(
                "policy.uat_invalid",
                "Maksimum 100 competency code per skenario.");
        }

        return new PolicyUatScenarioRequest(
            RequiredValue(scenario.CaseCode, "caseCode", 100),
            RequiredValue(scenario.Description, "description", 500),
            RequiredValue(scenario.SubjectId, "subjectId", 200),
            RequiredValue(scenario.ActionCode, "actionCode", 100),
            RequiredValue(scenario.LocationCode, "locationCode", 100),
            competencyCodes.Select(code => RequiredValue(code, "competencyCode", 100))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            scenario.EvaluatedAt?.ToUniversalTime(),
            outcome,
            string.IsNullOrWhiteSpace(scenario.ExpectedCode)
                ? null
                : RequiredValue(scenario.ExpectedCode, "expectedCode", 100));
    }

    private static string RequiredValue(string value, string field, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new InvalidRequestException(
                "policy.uat_invalid",
                $"{field} wajib diisi dan maksimum {maximumLength} karakter.");
        }

        return normalized;
    }

    private static void EnsureIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib dan maksimum 200 karakter untuk command paket UAT.");
        }
    }

    private async Task<PolicyUatSuiteEntry> GetSuiteEntryAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await store.FindSuiteAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Paket UAT policy", id);

    private Actor EnsureAdministrator()
    {
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Peran Administrator diperlukan untuk mengelola UAT policy.");
        }

        return actor;
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private static PolicyUatSuiteResponse ToResponse(PolicyUatSuiteEntry entry) => new(
        entry.Id,
        entry.SuiteKey,
        entry.Name,
        entry.PolicyVersion,
        entry.Version,
        entry.Scenarios,
        entry.ContentHash,
        entry.CreatedAt,
        entry.CreatedBy,
        entry.LatestRun);

    private static PolicyUatRunResponse ToResponse(PolicyUatRunEntry entry) => new(
        entry.Id,
        entry.SuiteId,
        entry.SuiteKey,
        entry.SuiteVersion,
        entry.PolicyVersion,
        entry.SuiteContentHash,
        entry.Passed,
        entry.Coverage,
        entry.Results,
        entry.ReportHash,
        entry.ExecutedAt,
        entry.ExecutedBy);
}
