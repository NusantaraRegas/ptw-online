using Ptw.Contracts;

namespace Ptw.Application;

public sealed record PolicyUatSuiteEntry(
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

public sealed record PolicyUatRunEntry(
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

public sealed record PolicyUatCommandContext(
    string ActorId,
    string Operation,
    string Key,
    string RequestHash);

public interface IPolicyUatStore
{
    Task<IReadOnlyList<PolicyUatSuiteEntry>> ListSuitesAsync(CancellationToken cancellationToken);
    Task<PolicyUatSuiteEntry?> FindSuiteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PolicyUatRunEntry>> ListRunsAsync(Guid suiteId, CancellationToken cancellationToken);
    Task<PolicyUatSuiteEntry?> FindSuiteCommandResultAsync(
        PolicyUatCommandContext command,
        CancellationToken cancellationToken);
    Task<PolicyUatRunEntry?> FindRunCommandResultAsync(
        PolicyUatCommandContext command,
        CancellationToken cancellationToken);
    Task<PolicyUatSuiteEntry> AddSuiteAsync(
        PolicyUatSuiteDraftRequest draft,
        Actor actor,
        DateTimeOffset createdAt,
        string correlationId,
        PolicyUatCommandContext command,
        CancellationToken cancellationToken);
    Task<PolicyUatRunEntry> AddRunAsync(
        PolicyUatSuiteEntry suite,
        bool passed,
        PolicyUatCoverageResponse coverage,
        IReadOnlyList<PolicyUatScenarioResultResponse> results,
        Actor actor,
        DateTimeOffset executedAt,
        string correlationId,
        PolicyUatCommandContext command,
        CancellationToken cancellationToken);
    Task<PolicyUatRunSummaryResponse?> FindLatestPassingRunAsync(
        string policyVersion,
        CancellationToken cancellationToken);
}
