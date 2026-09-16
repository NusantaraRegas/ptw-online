using Microsoft.EntityFrameworkCore;
using Ptw.Application;

namespace Ptw.Infrastructure.Persistence;

public sealed class PrintPackageStore(PtwDbContext dbContext, GeneratedDocumentSettings settings)
    : IPrintPackageStore
{
    /// <summary>
    /// How long a claimed render is considered in flight. A worker that dies mid-render leaves the row
    /// claimed only until the lease lapses, after which another instance picks it up.
    /// </summary>
    private static readonly TimeSpan RenderLease = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<PrintPackageDocumentEntry>> ListForPermitAsync(
        Guid permitId,
        CancellationToken cancellationToken)
    {
        var rows = await Query()
            .Where(x => x.Snapshot.PermitId == permitId)
            .OrderByDescending(x => x.Snapshot.PermitVersion)
            .ToListAsync(cancellationToken);
        return rows.ConvertAll(Map);
    }

    public async Task<PrintPackageDocumentEntry?> FindAsync(
        Guid permitId,
        Guid printPackageId,
        CancellationToken cancellationToken)
    {
        var row = await Query()
            .SingleOrDefaultAsync(
                x => x.Snapshot.Id == printPackageId && x.Snapshot.PermitId == permitId,
                cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<string?> FindSnapshotJsonAsync(Guid printPackageId, CancellationToken cancellationToken) =>
        await dbContext.PrintPackageSnapshots.AsNoTracking()
            .Where(x => x.Id == printPackageId)
            .Select(x => x.SnapshotJson)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> RequestRetryAsync(
        Guid printPackageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.PrintPackageSnapshots
            .SingleOrDefaultAsync(x => x.Id == printPackageId, cancellationToken);
        if (snapshot is null)
        {
            return false;
        }

        var document = await dbContext.GeneratedDocuments
            .SingleOrDefaultAsync(x => x.PrintPackageSnapshotId == printPackageId, cancellationToken);
        if (document is null)
        {
            return false;
        }

        // Requeueing an already pending render is a no-op so a repeated support action stays idempotent.
        if (string.Equals(document.RenderStatus, "PENDING", StringComparison.Ordinal))
        {
            return true;
        }

        document.RenderStatus = "PENDING";
        document.NextAttemptAt = now;
        document.Attempts = 0;
        document.LastError = null;
        snapshot.RenderStatus = "PENDING";
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PrintPackageRenderJob?> ClaimNextAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var due = await dbContext.GeneratedDocuments
            .Where(x => (x.RenderStatus == "PENDING" || x.RenderStatus == "RETRYING")
                && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.NextAttemptAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (due is null)
        {
            return null;
        }

        var snapshot = await dbContext.PrintPackageSnapshots.AsNoTracking()
            .SingleAsync(x => x.Id == due.PrintPackageSnapshotId, cancellationToken);

        // Take the lease under the row version, so a competing worker loses and simply retries later.
        due.RenderStatus = "RETRYING";
        due.NextAttemptAt = now.Add(RenderLease);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return null;
        }

        return new PrintPackageRenderJob(
            due.Id,
            snapshot.Id,
            snapshot.PermitId,
            snapshot.PermitVersion,
            snapshot.SnapshotJson,
            snapshot.SnapshotHash,
            due.Attempts);
    }

    public async Task CompleteRenderAsync(
        Guid generatedDocumentId,
        StoredGeneratedDocument stored,
        string mediaType,
        string rendererVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var document = await dbContext.GeneratedDocuments
            .SingleOrDefaultAsync(x => x.Id == generatedDocumentId, cancellationToken)
            ?? throw new ResourceNotFoundException("Generated document", generatedDocumentId);

        document.StorageKey = stored.StorageKey;
        document.MediaType = mediaType;
        document.SizeBytes = stored.SizeBytes;
        document.Sha256 = stored.Sha256;
        document.RenderStatus = "READY";
        document.RendererVersion = rendererVersion;
        document.GeneratedAt = now;
        document.NextAttemptAt = null;
        document.LastError = null;
        document.Attempts += 1;

        // The closure guard reads the snapshot status, so both rows advance together.
        var snapshot = await dbContext.PrintPackageSnapshots
            .SingleAsync(x => x.Id == document.PrintPackageSnapshotId, cancellationToken);
        snapshot.RenderStatus = "READY";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailRenderAsync(
        Guid generatedDocumentId,
        string failureReason,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.GeneratedDocuments
            .SingleOrDefaultAsync(x => x.Id == generatedDocumentId, cancellationToken)
            ?? throw new ResourceNotFoundException("Generated document", generatedDocumentId);

        document.Attempts += 1;
        document.LastError = Truncate(failureReason, 2000);
        var exhausted = document.Attempts >= settings.MaxRenderAttempts || nextAttemptAt is null;
        document.RenderStatus = exhausted ? "FAILED" : "RETRYING";
        document.NextAttemptAt = exhausted ? null : nextAttemptAt;

        var snapshot = await dbContext.PrintPackageSnapshots
            .SingleAsync(x => x.Id == document.PrintPackageSnapshotId, cancellationToken);
        snapshot.RenderStatus = document.RenderStatus;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordDownloadAsync(
        Guid permitId,
        Guid printPackageId,
        string actorId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.CreateVersion7(),
            PermitId = permitId,
            EventType = "print_package_downloaded",
            ActorId = actorId,
            OccurredAt = now,
            PayloadJson = $"{{\"printPackageId\":\"{printPackageId}\"}}",
            CorrelationId = correlationId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<SnapshotDocument> Query() =>
        from snapshot in dbContext.PrintPackageSnapshots.AsNoTracking()
        join document in dbContext.GeneratedDocuments.AsNoTracking()
            on snapshot.Id equals document.PrintPackageSnapshotId
        select new SnapshotDocument { Snapshot = snapshot, Document = document };

    private static PrintPackageDocumentEntry Map(SnapshotDocument row) => new(
        row.Snapshot.Id,
        row.Snapshot.PermitId,
        row.Snapshot.PermitVersion,
        row.Document.Id,
        row.Document.RenderStatus,
        row.Document.Attempts,
        row.Document.LastError,
        row.Document.StorageKey,
        row.Document.MediaType,
        row.Document.SizeBytes,
        row.Document.Sha256,
        row.Snapshot.PrintTemplateVersion,
        row.Snapshot.CampaignAssetVersion,
        row.Snapshot.SnapshotHash,
        row.Snapshot.CreatedAt,
        row.Document.GeneratedAt);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class SnapshotDocument
    {
        public required PrintPackageSnapshotRecord Snapshot { get; init; }
        public required GeneratedDocumentRecord Document { get; init; }
    }
}
