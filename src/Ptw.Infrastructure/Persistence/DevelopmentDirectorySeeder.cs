using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;

namespace Ptw.Infrastructure.Persistence;

/// <summary>
/// Whether the Development bootstrap seed may run. Bound to the hosting environment at
/// registration time so the seeder itself refuses outside Development, independently of the
/// caller's check.
/// </summary>
public sealed record DevelopmentSeedSettings(bool Enabled);

public sealed record DevelopmentSeedResult(bool Enabled, IReadOnlyList<string> SeededSubjectIds, int SkippedWithCredential)
{
    public static DevelopmentSeedResult Disabled { get; } = new(false, [], 0);
}

/// <summary>
/// Gives every account created by the user directory seed migration the shared Development
/// password, so a developer can log in as any seeded user right after `--migrate`. This is not a
/// migration on purpose: the seed migrations run in every environment, and a well-known password
/// on the real staff accounts of production would bypass Portal authentication. The seeder runs
/// only on the `--migrate` path of a Development host, is idempotent (accounts that already hold a
/// local credential, including one set by an operator, are never overwritten), and ignores accounts
/// an operator registered.
/// </summary>
public sealed class DevelopmentDirectorySeeder(PtwDbContext dbContext, IClock clock, DevelopmentSeedSettings settings)
{
    /// <summary>Shared local password of every seeded account in Development.</summary>
    public const string SeededLocalPassword = "Nus@nt@r@h3b4t";
    public const string Actor = "system:development-seed";
    public const string CorrelationId = "development-seed:local-credentials";
    public const string EventType = "user_credential_seeded";
    private const string SeededAccountEventType = "user_account_seeded";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DevelopmentSeedResult> SeedLocalCredentialsAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return DevelopmentSeedResult.Disabled;
        }

        var seededAggregateIds = await dbContext.ConfigurationAuditEvents.AsNoTracking()
            .Where(x => x.AggregateType == "UserAccount" && x.EventType == SeededAccountEventType)
            .Select(x => x.AggregateId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var seededAggregates = seededAggregateIds.ToHashSet();
        var activeSubjects = await dbContext.UserAccounts.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.SubjectId)
            .ToListAsync(cancellationToken);
        var subjectsWithCredential = (await dbContext.UserCredentials.AsNoTracking()
            .Select(x => x.SubjectId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var seededSubjects = activeSubjects
            .Where(subjectId => seededAggregates.Contains(UserDirectoryStore.DeterministicGuid(subjectId)))
            .Order(StringComparer.Ordinal)
            .ToList();
        var targets = seededSubjects.Where(subjectId => !subjectsWithCredential.Contains(subjectId)).ToList();
        var now = clock.UtcNow;
        foreach (var subjectId in targets)
        {
            var (salt, hash) = UserDirectoryStore.HashPassword(SeededLocalPassword);
            dbContext.UserCredentials.Add(new UserCredentialRecord
            {
                SubjectId = subjectId,
                PasswordSalt = salt,
                PasswordHash = hash,
                Iterations = UserDirectoryStore.PasswordIterations,
                FailedAttempts = 0,
                LockedUntil = null,
                PasswordChangedAt = now
            });
            var aggregateId = UserDirectoryStore.DeterministicGuid(subjectId);
            // The payload records that a credential was seeded, never the password itself.
            var payloadJson = JsonSerializer.Serialize(new { SubjectId = subjectId, Source = "development-seed" }, JsonOptions);
            dbContext.ConfigurationAuditEvents.Add(new ConfigurationAuditEventRecord
            {
                Id = Guid.CreateVersion7(),
                AggregateType = "UserAccount",
                AggregateId = aggregateId,
                EventType = EventType,
                ActorId = Actor,
                OccurredAt = now,
                PayloadJson = payloadJson,
                CorrelationId = CorrelationId
            });
            dbContext.OutboxMessages.Add(new OutboxMessageRecord
            {
                Id = Guid.CreateVersion7(),
                AggregateId = aggregateId,
                EventType = EventType,
                PayloadJson = payloadJson,
                OccurredAt = now
            });
        }

        if (targets.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new DevelopmentSeedResult(true, targets, seededSubjects.Count - targets.Count);
    }
}
