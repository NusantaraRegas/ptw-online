using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Persistence;

public sealed class LocationMasterStore(PtwDbContext dbContext) : ILocationMasterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StoredLocationMaster>> ListAsync(CancellationToken cancellationToken)
    {
        var records = await dbContext.LocationMasters.AsNoTracking()
            .OrderBy(x => x.Code)
            .ThenByDescending(x => x.EffectiveFrom)
            .Take(500)
            .ToListAsync(cancellationToken);
        return records.Select(ToStored).ToArray();
    }

    public async Task<IReadOnlyList<StoredLocationMaster>> FindApprovedEffectiveByCodeAsync(
        string code,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        var normalizedCode = code.Trim();
        var utcInstant = instant.ToUniversalTime();
        var records = await dbContext.LocationMasters.AsNoTracking()
            .Where(x => x.Code == normalizedCode
                && x.Status == LocationMasterStatus.Approved.ToString()
                && x.EffectiveFrom <= utcInstant
                && (x.EffectiveUntil == null || x.EffectiveUntil > utcInstant))
            .OrderByDescending(x => x.EffectiveFrom)
            .Take(2)
            .ToListAsync(cancellationToken);
        return records.Select(ToStored).ToArray();
    }

    public Task<int> CountApprovedEffectiveAsync(
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        var utcInstant = instant.ToUniversalTime();
        return dbContext.LocationMasters.AsNoTracking().CountAsync(
            x => x.Status == LocationMasterStatus.Approved.ToString()
                && x.EffectiveFrom <= utcInstant
                && (x.EffectiveUntil == null || x.EffectiveUntil > utcInstant),
            cancellationToken);
    }

    public async Task<StoredLocationMaster?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.LocationMasters.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return record is null ? null : ToStored(record);
    }

    public async Task<StoredLocationMaster> AddAsync(
        LocationMasterEntry entry,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var record = ToRecord(entry);
        dbContext.LocationMasters.Add(record);
        AddVersion(entry, actor.Id);
        AddEvents(entry.DequeueEvents(), actor.Id, correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(record);
    }

    public async Task<StoredLocationMaster> UpdateAsync(
        LocationMasterEntry entry,
        string expectedETag,
        Actor actor,
        string correlationId,
        LocationCommandContext? command,
        CancellationToken cancellationToken)
    {
        var expectedVersion = DecodeETag(expectedETag);
        var record = await dbContext.LocationMasters.SingleAsync(x => x.Id == entry.Id, cancellationToken);
        if (record.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException();
        }

        Apply(record, entry);
        AddVersion(entry, actor.Id);
        AddEvents(entry.DequeueEvents(), actor.Id, correlationId);
        if (command is not null)
        {
            dbContext.LocationCommandReceipts.Add(new LocationCommandReceiptRecord
            {
                Id = Guid.CreateVersion7(),
                ActorId = command.ActorId,
                Operation = command.Operation,
                Key = command.Key,
                RequestHash = command.RequestHash,
                LocationMasterId = entry.Id,
                ResultVersion = entry.Version,
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

    public async Task<StoredLocationMaster?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.LocationCommandReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ActorId == actorId && x.Operation == operation && x.Key == key,
                cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(receipt.RequestHash),
                Convert.FromHexString(requestHash)))
        {
            throw new InvalidRequestException(
                "idempotency.payload_mismatch",
                "Idempotency-Key telah digunakan dengan payload berbeda.");
        }

        var version = await dbContext.LocationMasterVersions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.LocationMasterId == receipt.LocationMasterId && x.Version == receipt.ResultVersion,
                cancellationToken)
            ?? throw new InvalidOperationException("Snapshot hasil command master lokasi tidak ditemukan.");
        var snapshot = JsonSerializer.Deserialize<LocationSnapshot>(version.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot hasil command master lokasi tidak valid.");
        return ToStored(snapshot);
    }

    private void AddVersion(LocationMasterEntry entry, string actorId)
    {
        var json = JsonSerializer.Serialize(ToSnapshot(entry), JsonOptions);
        dbContext.LocationMasterVersions.Add(new LocationMasterVersionRecord
        {
            Id = Guid.CreateVersion7(),
            LocationMasterId = entry.Id,
            Version = entry.Version,
            ContentJson = json,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            CreatedAt = entry.UpdatedAt,
            CreatedBy = actorId
        });
    }

    private void AddEvents(IReadOnlyList<MasterDataEvent> events, string actorId, string correlationId)
    {
        foreach (var domainEvent in events)
        {
            var payload = JsonSerializer.Serialize(domainEvent.Payload, JsonOptions);
            dbContext.ConfigurationAuditEvents.Add(new ConfigurationAuditEventRecord
            {
                Id = domainEvent.Id,
                AggregateType = "LocationMaster",
                AggregateId = domainEvent.AggregateId,
                EventType = domainEvent.Type,
                ActorId = actorId,
                OccurredAt = domainEvent.OccurredAt,
                PayloadJson = payload,
                CorrelationId = correlationId
            });
            dbContext.OutboxMessages.Add(new OutboxMessageRecord
            {
                Id = domainEvent.Id,
                AggregateId = domainEvent.AggregateId,
                EventType = domainEvent.Type,
                PayloadJson = payload,
                OccurredAt = domainEvent.OccurredAt
            });
        }
    }

    private static LocationMasterRecord ToRecord(LocationMasterEntry entry) => new()
    {
        Id = entry.Id,
        Code = entry.Code,
        Name = entry.Name,
        ParentId = entry.ParentId,
        EffectiveFrom = entry.EffectiveFrom,
        EffectiveUntil = entry.EffectiveUntil,
        Status = entry.Status.ToString(),
        Version = entry.Version,
        MakerId = entry.MakerId,
        CheckerId = entry.CheckerId,
        ApprovedAt = entry.ApprovedAt,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };

    private static void Apply(LocationMasterRecord record, LocationMasterEntry entry)
    {
        record.Code = entry.Code;
        record.Name = entry.Name;
        record.ParentId = entry.ParentId;
        record.EffectiveFrom = entry.EffectiveFrom;
        record.EffectiveUntil = entry.EffectiveUntil;
        record.Status = entry.Status.ToString();
        record.Version = entry.Version;
        record.MakerId = entry.MakerId;
        record.CheckerId = entry.CheckerId;
        record.ApprovedAt = entry.ApprovedAt;
        record.UpdatedAt = entry.UpdatedAt;
    }

    private static StoredLocationMaster ToStored(LocationMasterRecord record) => new(
        LocationMasterEntry.Rehydrate(
            record.Id,
            record.Code,
            record.Name,
            record.ParentId,
            record.EffectiveFrom,
            record.EffectiveUntil,
            Enum.Parse<LocationMasterStatus>(record.Status),
            record.Version,
            record.MakerId,
            record.CheckerId,
            record.ApprovedAt,
            record.CreatedAt,
            record.UpdatedAt),
        EncodeETag(record.Version));

    private static LocationSnapshot ToSnapshot(LocationMasterEntry entry) => new(
        entry.Id,
        entry.Code,
        entry.Name,
        entry.ParentId,
        entry.EffectiveFrom,
        entry.EffectiveUntil,
        entry.Status,
        entry.Version,
        entry.MakerId,
        entry.CheckerId,
        entry.ApprovedAt,
        entry.CreatedAt,
        entry.UpdatedAt);

    private static StoredLocationMaster ToStored(LocationSnapshot snapshot) => new(
        LocationMasterEntry.Rehydrate(
            snapshot.Id,
            snapshot.Code,
            snapshot.Name,
            snapshot.ParentId,
            snapshot.EffectiveFrom,
            snapshot.EffectiveUntil,
            snapshot.Status,
            snapshot.Version,
            snapshot.MakerId,
            snapshot.CheckerId,
            snapshot.ApprovedAt,
            snapshot.CreatedAt,
            snapshot.UpdatedAt),
        EncodeETag(snapshot.Version));

    private static string EncodeETag(int version) => $"\"location-v{version}\"";

    private static int DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidRequestException(
                "concurrency.if_match_required",
                "Header If-Match wajib untuk perubahan master lokasi.");
        }

        try
        {
            var value = etag.Trim().Trim('"');
            return value.StartsWith("location-v", StringComparison.Ordinal)
                && int.TryParse(value[10..], out var version)
                && version > 0
                    ? version
                    : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidRequestException("concurrency.invalid_etag", "Format ETag tidak valid.");
        }
    }

    private sealed record LocationSnapshot(
        Guid Id,
        string Code,
        string Name,
        Guid? ParentId,
        DateTimeOffset EffectiveFrom,
        DateTimeOffset? EffectiveUntil,
        LocationMasterStatus Status,
        int Version,
        string MakerId,
        string? CheckerId,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
