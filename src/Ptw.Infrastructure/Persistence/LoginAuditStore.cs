using Microsoft.EntityFrameworkCore;
using Ptw.Application;

namespace Ptw.Infrastructure.Persistence;

/// <summary>
/// Append-only journal of login attempts. Rows are only ever inserted; there is deliberately no
/// update or delete path so the journal stays usable as evidence.
/// </summary>
public sealed class LoginAuditStore(PtwDbContext dbContext) : ILoginAuditStore
{
    public async Task RecordAsync(LoginAuditEntry entry, CancellationToken cancellationToken)
    {
        dbContext.LoginAuditEvents.Add(new LoginAuditEventRecord
        {
            Id = entry.Id,
            OccurredAt = entry.OccurredAt,
            UserName = entry.UserName,
            SubjectId = entry.SubjectId,
            DirectoryResult = entry.DirectoryResult,
            IdentitySource = entry.IdentitySource,
            Outcome = entry.Outcome,
            SourceAddress = entry.SourceAddress.Length > 64 ? entry.SourceAddress[..64] : entry.SourceAddress,
            CorrelationId = entry.CorrelationId.Length > 100 ? entry.CorrelationId[..100] : entry.CorrelationId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LoginAuditEntry>> ListRecentAsync(
        int limit,
        string? subjectId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LoginAuditEvents.AsNoTracking();
        if (subjectId is not null)
        {
            query = query.Where(x => x.SubjectId == subjectId);
        }

        var rows = await query
            .OrderByDescending(x => x.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new LoginAuditEntry(
            x.Id,
            x.OccurredAt,
            x.UserName,
            x.SubjectId,
            x.DirectoryResult,
            x.IdentitySource,
            x.Outcome,
            x.SourceAddress,
            x.CorrelationId)).ToArray();
    }
}
