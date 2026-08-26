using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Persistence;

public sealed class PermitStore(PtwDbContext dbContext) : IPermitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoredPermit?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.Permits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return record is null ? null : ToStored(record);
    }

    public async Task<IReadOnlyList<StoredPermit>> ListAsync(string? sponsorId, CancellationToken cancellationToken)
    {
        var query = dbContext.Permits.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sponsorId))
        {
            query = query.Where(x => x.SponsorId == sponsorId);
        }

        var records = await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        return records.Select(ToStored).ToArray();
    }

    public async Task<StoredPermit> AddAsync(Permit permit, Actor actor, string correlationId, CancellationToken cancellationToken)
    {
        var record = ToRecord(permit);
        dbContext.Permits.Add(record);
        AddVersion(permit, actor.Id);
        AddEvents(permit.DequeueEvents(), actor.Id, correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(record);
    }

    public async Task<StoredPermit> UpdateAsync(
        Permit permit,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        var persistedVersion = await dbContext.Permits.AsNoTracking()
            .Where(x => x.Id == permit.Id)
            .Select(x => x.Version)
            .SingleAsync(cancellationToken);
        var record = ToRecord(permit);
        record.RowVersion = DecodeETag(expectedETag);
        dbContext.Permits.Attach(record);
        dbContext.Entry(record).State = EntityState.Modified;
        dbContext.Entry(record).Property(x => x.RowVersion).OriginalValue = record.RowVersion;
        if (permit.Version > persistedVersion)
        {
            AddVersion(permit, actor.Id);
        }
        AddEvents(permit.DequeueEvents(), actor.Id, correlationId);
        if (idempotency is not null)
        {
            dbContext.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = Guid.CreateVersion7(),
                ActorId = idempotency.ActorId,
                Operation = idempotency.Operation,
                Key = idempotency.Key,
                RequestHash = idempotency.RequestHash,
                PermitId = permit.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException() { Source = exception.Source };
        }

        return ToStored(record);
    }

    public async Task<StoredPermit?> FindIdempotentResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.Operation == operation && x.Key == key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.RequestHash),
                Convert.FromHexString(requestHash)))
        {
            throw new InvalidRequestException("idempotency.payload_mismatch", "Idempotency-Key telah digunakan dengan payload berbeda.");
        }

        return await FindAsync(existing.PermitId, cancellationToken);
    }

    private void AddVersion(Permit permit, string actorId)
    {
        var json = JsonSerializer.Serialize(permit.Draft, JsonOptions);
        dbContext.PermitVersions.Add(new PermitVersionRecord
        {
            Id = Guid.CreateVersion7(),
            PermitId = permit.Id,
            Version = permit.Version,
            ContentJson = json,
            ContentHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))),
            CreatedAt = permit.UpdatedAt,
            CreatedBy = actorId
        });
    }

    private void AddEvents(IReadOnlyList<DomainEvent> events, string actorId, string correlationId)
    {
        foreach (var domainEvent in events)
        {
            var payload = JsonSerializer.Serialize(domainEvent.Payload, JsonOptions);
            dbContext.AuditEvents.Add(new AuditEventRecord
            {
                Id = domainEvent.Id,
                PermitId = domainEvent.PermitId,
                EventType = domainEvent.Type,
                ActorId = actorId,
                OccurredAt = domainEvent.OccurredAt,
                PayloadJson = payload,
                CorrelationId = correlationId
            });
            dbContext.OutboxMessages.Add(new OutboxMessageRecord
            {
                Id = domainEvent.Id,
                AggregateId = domainEvent.PermitId,
                EventType = domainEvent.Type,
                PayloadJson = payload,
                OccurredAt = domainEvent.OccurredAt
            });
        }
    }

    private static PermitRecord ToRecord(Permit permit) => new()
    {
        Id = permit.Id,
        PermitNumber = permit.PermitNumber,
        Status = permit.Status.ToString(),
        Version = permit.Version,
        LocationId = permit.Draft.LocationId,
        SponsorId = permit.Draft.SponsorId,
        ValidFrom = permit.Draft.ValidFrom,
        ValidUntil = permit.Draft.ValidUntil,
        DraftJson = JsonSerializer.Serialize(permit.Draft, JsonOptions),
        CreatedAt = permit.CreatedAt,
        UpdatedAt = permit.UpdatedAt,
        ActiveWorkPeriodId = permit.ActiveWorkPeriodId,
        SuspensionReason = permit.SuspensionReason
    };

    private static StoredPermit ToStored(PermitRecord record)
    {
        var draft = JsonSerializer.Deserialize<PermitDraft>(record.DraftJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot draft PTW tidak valid.");
        var permit = Permit.Rehydrate(
            record.Id,
            record.PermitNumber,
            Enum.Parse<PermitStatus>(record.Status),
            record.Version,
            draft,
            record.CreatedAt,
            record.UpdatedAt,
            record.ActiveWorkPeriodId,
            record.SuspensionReason);
        return new StoredPermit(permit, EncodeETag(record.RowVersion));
    }

    private static string EncodeETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    private static byte[] DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidRequestException("concurrency.if_match_required", "Header If-Match wajib untuk perubahan PTW.");
        }

        try
        {
            return Convert.FromBase64String(etag.Trim().Trim('"'));
        }
        catch (FormatException)
        {
            throw new InvalidRequestException("concurrency.invalid_etag", "Format ETag tidak valid.");
        }
    }
}
