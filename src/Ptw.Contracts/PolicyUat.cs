namespace Ptw.Contracts;

public sealed record PolicyUatScenarioRequest(
    string CaseCode,
    string Description,
    string SubjectId,
    string ActionCode,
    string LocationCode,
    IReadOnlyList<string> CompetencyCodes,
    DateTimeOffset? EvaluatedAt,
    string ExpectedOutcome,
    string? ExpectedCode);

public sealed record PolicyUatSuiteDraftRequest(
    string SuiteKey,
    string Name,
    string PolicyVersion,
    IReadOnlyList<PolicyUatScenarioRequest> Scenarios);

public sealed record PolicyUatCoverageResponse(
    int ScenarioCount,
    int ExpectedAllowCount,
    int ExpectedDenyCount,
    int ActualAllowCount,
    int ActualDenyCount,
    int MatchedCount,
    int DistinctSubjectCount,
    int DistinctActionCount,
    int DistinctLocationCount,
    int DistinctRoleCount,
    int DistinctCompetencyCount,
    int TemporalScenarioCount);

public sealed record PolicyUatRunSummaryResponse(
    Guid Id,
    bool Passed,
    int MatchedCount,
    int ScenarioCount,
    string ReportHash,
    DateTimeOffset ExecutedAt,
    string ExecutedBy);

public sealed record PolicyUatSuiteResponse(
    Guid Id,
    string SuiteKey,
    string Name,
    string PolicyVersion,
    int Version,
    IReadOnlyList<PolicyUatScenarioRequest> Scenarios,
    string ContentHash,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    PolicyUatRunSummaryResponse? LatestRun);

public sealed record PolicyUatScenarioResultResponse(
    string CaseCode,
    string ExpectedOutcome,
    string? ExpectedCode,
    string ActualOutcome,
    string ActualCode,
    bool Matched,
    PolicySimulationResponse Actual);

public sealed record PolicyUatRunResponse(
    Guid Id,
    Guid SuiteId,
    string SuiteKey,
    int SuiteVersion,
    string PolicyVersion,
    string SuiteContentHash,
    bool Passed,
    PolicyUatCoverageResponse Coverage,
    IReadOnlyList<PolicyUatScenarioResultResponse> Results,
    string ReportHash,
    DateTimeOffset ExecutedAt,
    string ExecutedBy);
