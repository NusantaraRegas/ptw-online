using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Contracts;

namespace Ptw.Infrastructure.Persistence;

public sealed class PolicyUatStore(PtwDbContext dbContext) : IPolicyUatStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PolicyUatSuiteEntry>> ListSuitesAsync(
        CancellationToken cancellationToken)
    {
        var records = await dbContext.PolicyUatSuites.AsNoTracking()
            .OrderBy(x => x.SuiteKey)
            .ThenByDescending(x => x.Version)
            .Take(200)
            .ToListAsync(cancellationToken);
        var latestRuns = await LatestRunsAsync(records.Select(item => item.Id).ToArray(), cancellationToken);
        return records.Select(record => ToSuite(
            record,
            latestRuns.GetValueOrDefault(record.Id))).ToArray();
    }

    public async Task<PolicyUatSuiteEntry?> FindSuiteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.PolicyUatSuites.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var latestRun = await dbContext.PolicyUatRuns.AsNoTracking()
            .Where(x => x.PolicyUatSuiteId == id)
            .OrderByDescending(x => x.ExecutedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return ToSuite(record, latestRun);
    }

    public async Task<IReadOnlyList<PolicyUatRunEntry>> ListRunsAsync(
        Guid suiteId,
        CancellationToken cancellationToken)
    {
        var suite = await dbContext.PolicyUatSuites.AsNoTracking()
            .SingleAsync(x => x.Id == suiteId, cancellationToken);
        var records = await dbContext.PolicyUatRuns.AsNoTracking()
            .Where(x => x.PolicyUatSuiteId == suiteId)
            .OrderByDescending(x => x.ExecutedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return records.Select(record => ToRun(record, suite)).ToArray();
    }

    public async Task<PolicyUatSuiteEntry?> FindSuiteCommandResultAsync(
        PolicyUatCommandContext command,
        CancellationToken cancellationToken)
    {
        var receipt = await FindReceiptAsync(command, cancellationToken);
        return receipt?.PolicyUatSuiteId is Guid suiteId
            ? await FindSuiteAsync(suiteId, cancellationToken)
            : null;
    }

    public async Task<PolicyUatRunEntry?> FindRunCommandResultAsync(
        PolicyUatCommandContext command,
        CancellationToken cancellationToken)
    {
        var receipt = await FindReceiptAsync(command, cancellationToken);
        if (receipt?.PolicyUatRunId is not Guid runId)
        {
            return null;
        }

        var run = await dbContext.PolicyUatRuns.AsNoTracking()
            .SingleAsync(x => x.Id == runId, cancellationToken);
        var suite = await dbContext.PolicyUatSuites.AsNoTracking()
            .SingleAsync(x => x.Id == run.PolicyUatSuiteId, cancellationToken);
        return ToRun(run, suite);
    }

    public async Task<PolicyUatSuiteEntry> AddSuiteAsync(
        PolicyUatSuiteDraftRequest draft,
        Actor actor,
        DateTimeOffset createdAt,
        string correlationId,
        PolicyUatCommandContext command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var nextVersion = (await dbContext.PolicyUatSuites
            .Where(x => x.SuiteKey == draft.SuiteKey)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var scenariosJson = JsonSerializer.Serialize(draft.Scenarios, JsonOptions);
        var contentHash = Hash(JsonSerializer.Serialize(draft, JsonOptions));
        var record = new PolicyUatSuiteRecord
        {
            Id = Guid.CreateVersion7(),
            SuiteKey = draft.SuiteKey,
            Name = draft.Name,
            PolicyVersion = draft.PolicyVersion,
            Version = nextVersion,
            ScenariosJson = scenariosJson,
            ContentHash = contentHash,
            CreatedAt = createdAt,
            CreatedBy = actor.Id
        };
        dbContext.PolicyUatSuites.Add(record);
        AddEvidence(
            "PolicyUatSuite",
            record.Id,
            "PolicyUatSuiteCreated",
            actor.Id,
            createdAt,
            correlationId,
            new
            {
                record.SuiteKey,
                record.Version,
                record.PolicyVersion,
                ScenarioCount = draft.Scenarios.Count,
                record.ContentHash
            });
        AddReceipt(command, record.Id, null, createdAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSuite(record, null);
    }

    public async Task<PolicyUatRunEntry> AddRunAsync(
        PolicyUatSuiteEntry suite,
        bool passed,
        PolicyUatCoverageResponse coverage,
        IReadOnlyList<PolicyUatScenarioResultResponse> results,
        Actor actor,
        DateTimeOffset executedAt,
        string correlationId,
        PolicyUatCommandContext command,
        CancellationToken cancellationToken)
    {
        var coverageJson = JsonSerializer.Serialize(coverage, JsonOptions);
        var resultsJson = JsonSerializer.Serialize(results, JsonOptions);
        var reportPayload = JsonSerializer.Serialize(new
        {
            suite.Id,
            suite.SuiteKey,
            SuiteVersion = suite.Version,
            suite.PolicyVersion,
            SuiteContentHash = suite.ContentHash,
            Passed = passed,
            Coverage = coverage,
            Results = results,
            ExecutedAt = executedAt,
            ExecutedBy = actor.Id
        }, JsonOptions);
        var record = new PolicyUatRunRecord
        {
            Id = Guid.CreateVersion7(),
            PolicyUatSuiteId = suite.Id,
            PolicyVersion = suite.PolicyVersion,
            SuiteContentHash = suite.ContentHash,
            Passed = passed,
            ScenarioCount = coverage.ScenarioCount,
            MatchedCount = coverage.MatchedCount,
            CoverageJson = coverageJson,
            ResultsJson = resultsJson,
            ReportHash = Hash(reportPayload),
            ExecutedAt = executedAt,
            ExecutedBy = actor.Id
        };
        dbContext.PolicyUatRuns.Add(record);
        AddEvidence(
            "PolicyUatSuite",
            suite.Id,
            "PolicyUatRunCompleted",
            actor.Id,
            executedAt,
            correlationId,
            new
            {
                RunId = record.Id,
                suite.SuiteKey,
                SuiteVersion = suite.Version,
                suite.PolicyVersion,
                record.Passed,
                record.ScenarioCount,
                record.MatchedCount,
                record.ReportHash
            });
        AddReceipt(command, null, record.Id, executedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRun(record, new PolicyUatSuiteRecord
        {
            Id = suite.Id,
            SuiteKey = suite.SuiteKey,
            Name = suite.Name,
            PolicyVersion = suite.PolicyVersion,
            Version = suite.Version,
            ScenariosJson = JsonSerializer.Serialize(suite.Scenarios, JsonOptions),
            ContentHash = suite.ContentHash,
            CreatedAt = suite.CreatedAt,
            CreatedBy = suite.CreatedBy
        });
    }

    public async Task<PolicyUatRunSummaryResponse?> FindLatestPassingRunAsync(
        string policyVersion,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.PolicyUatRuns.AsNoTracking()
            .Where(x => EF.Functions.Collate(x.PolicyVersion, "Latin1_General_100_BIN2") == policyVersion
                && x.Passed)
            .OrderByDescending(x => x.ExecutedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : ToSummary(record);
    }

    private async Task<PolicyUatCommandReceiptRecord?> FindReceiptAsync(
        PolicyUatCommandContext command,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.PolicyUatCommandReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ActorId == command.ActorId
                    && x.Operation == command.Operation
                    && x.Key == command.Key,
                cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(receipt.RequestHash),
                Convert.FromHexString(command.RequestHash)))
        {
            throw new InvalidRequestException(
                "idempotency.payload_mismatch",
                "Idempotency-Key telah digunakan dengan payload berbeda.");
        }

        return receipt;
    }

    private async Task<Dictionary<Guid, PolicyUatRunRecord>> LatestRunsAsync(
        Guid[] suiteIds,
        CancellationToken cancellationToken)
    {
        if (suiteIds.Length == 0)
        {
            return [];
        }

        var runs = await dbContext.PolicyUatRuns.AsNoTracking()
            .Where(x => suiteIds.Contains(x.PolicyUatSuiteId))
            .OrderByDescending(x => x.ExecutedAt)
            .ToListAsync(cancellationToken);
        return runs.GroupBy(x => x.PolicyUatSuiteId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private void AddReceipt(
        PolicyUatCommandContext command,
        Guid? suiteId,
        Guid? runId,
        DateTimeOffset createdAt) =>
        dbContext.PolicyUatCommandReceipts.Add(new PolicyUatCommandReceiptRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = command.ActorId,
            Operation = command.Operation,
            Key = command.Key,
            RequestHash = command.RequestHash,
            PolicyUatSuiteId = suiteId,
            PolicyUatRunId = runId,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddHours(24)
        });

    private void AddEvidence(
        string aggregateType,
        Guid aggregateId,
        string eventType,
        string actorId,
        DateTimeOffset occurredAt,
        string correlationId,
        object evidence)
    {
        var eventId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(evidence, JsonOptions);
        dbContext.ConfigurationAuditEvents.Add(new ConfigurationAuditEventRecord
        {
            Id = eventId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            ActorId = actorId,
            OccurredAt = occurredAt,
            PayloadJson = payload,
            CorrelationId = correlationId
        });
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = eventId,
            AggregateId = aggregateId,
            EventType = eventType,
            PayloadJson = payload,
            OccurredAt = occurredAt
        });
    }

    private static PolicyUatSuiteEntry ToSuite(
        PolicyUatSuiteRecord record,
        PolicyUatRunRecord? latestRun) => new(
        record.Id,
        record.SuiteKey,
        record.Name,
        record.PolicyVersion,
        record.Version,
        Deserialize<PolicyUatScenarioRequest[]>(record.ScenariosJson, "Skenario paket UAT"),
        record.ContentHash,
        record.CreatedAt,
        record.CreatedBy,
        latestRun is null ? null : ToSummary(latestRun));

    private static PolicyUatRunEntry ToRun(
        PolicyUatRunRecord record,
        PolicyUatSuiteRecord suite) => new(
        record.Id,
        record.PolicyUatSuiteId,
        suite.SuiteKey,
        suite.Version,
        record.PolicyVersion,
        record.SuiteContentHash,
        record.Passed,
        Deserialize<PolicyUatCoverageResponse>(record.CoverageJson, "Coverage UAT"),
        Deserialize<PolicyUatScenarioResultResponse[]>(record.ResultsJson, "Hasil UAT"),
        record.ReportHash,
        record.ExecutedAt,
        record.ExecutedBy);

    private static PolicyUatRunSummaryResponse ToSummary(PolicyUatRunRecord record) => new(
        record.Id,
        record.Passed,
        record.MatchedCount,
        record.ScenarioCount,
        record.ReportHash,
        record.ExecutedAt,
        record.ExecutedBy);

    private static T Deserialize<T>(string json, string label) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"{label} tidak valid.");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
