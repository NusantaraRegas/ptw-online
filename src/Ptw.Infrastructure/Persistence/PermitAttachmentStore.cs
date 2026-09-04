using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Persistence;

public sealed class PermitAttachmentStore(
    PtwDbContext dbContext,
    IPermitStore permitStore) : IPermitAttachmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PermitAttachmentEntry>> ListActiveAsync(
        Guid permitId,
        CancellationToken cancellationToken) =>
        (await dbContext.PermitAttachments.AsNoTracking()
            .Where(x => x.PermitId == permitId && x.RemovedInVersion == null)
            .OrderBy(x => x.UploadedAt)
            .ToListAsync(cancellationToken))
        .Select(ToEntry)
        .ToArray();

    public async Task<PermitAttachmentEntry?> FindActiveAsync(
        Guid permitId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.PermitAttachments.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == attachmentId && x.PermitId == permitId && x.RemovedInVersion == null,
            cancellationToken);
        return record is null ? null : ToEntry(record);
    }

    public async Task<StoredPermitAttachment?> FindIdempotentResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.PermitAttachmentCommandReceipts.AsNoTracking()
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

        var permit = await permitStore.FindAsync(receipt.PermitId, cancellationToken)
            ?? throw new InvalidOperationException("Hasil idempotensi menunjuk PTW yang tidak tersedia.");
        var attachment = await dbContext.PermitAttachments.AsNoTracking()
            .SingleAsync(x => x.Id == receipt.AttachmentId, cancellationToken);
        return new StoredPermitAttachment(permit, ToEntry(attachment));
    }

    public Task<StoredPermitAttachment> AddAsync(
        Permit permit,
        PermitAttachmentEntry attachment,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        CancellationToken cancellationToken) =>
        SaveAsync(
            permit,
            attachment,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            false,
            cancellationToken);

    public Task<StoredPermitAttachment> RemoveAsync(
        Permit permit,
        PermitAttachmentEntry attachment,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        CancellationToken cancellationToken) =>
        SaveAsync(
            permit,
            attachment,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            true,
            cancellationToken);

    private async Task<StoredPermitAttachment> SaveAsync(
        Permit permit,
        PermitAttachmentEntry attachment,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        bool removing,
        CancellationToken cancellationToken)
    {
        var permitRecord = await dbContext.Permits.SingleAsync(x => x.Id == permit.Id, cancellationToken);
        dbContext.Entry(permitRecord).Property(x => x.RowVersion).OriginalValue = DecodeETag(expectedETag);
        permitRecord.Version = permit.Version;
        permitRecord.UpdatedAt = permit.UpdatedAt;

        PermitAttachmentRecord attachmentRecord;
        if (removing)
        {
            attachmentRecord = await dbContext.PermitAttachments.SingleAsync(
                x => x.Id == attachment.Id && x.PermitId == permit.Id && x.RemovedInVersion == null,
                cancellationToken);
            attachmentRecord.RemovedInVersion = attachment.RemovedInVersion;
            attachmentRecord.RemovedBy = actor.Id;
            attachmentRecord.RemovedAt = permit.UpdatedAt;
        }
        else
        {
            attachmentRecord = ToRecord(attachment);
            dbContext.PermitAttachments.Add(attachmentRecord);
        }

        AddVersion(permit, actor.Id);
        AddEvents(permit.DequeueEvents(), actor.Id, correlationId);
        dbContext.PermitAttachmentCommandReceipts.Add(new PermitAttachmentCommandReceiptRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = idempotency.ActorId,
            Operation = idempotency.Operation,
            Key = idempotency.Key,
            RequestHash = idempotency.RequestHash,
            PermitId = permit.Id,
            AttachmentId = attachment.Id,
            ResultVersion = permit.Version,
            CreatedAt = permit.UpdatedAt,
            ExpiresAt = permit.UpdatedAt.AddHours(24)
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException() { Source = exception.Source };
        }

        return new StoredPermitAttachment(
            new StoredPermit(permit, EncodeETag(permitRecord.RowVersion)),
            ToEntry(attachmentRecord));
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
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
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

    private static PermitAttachmentRecord ToRecord(PermitAttachmentEntry entry) => new()
    {
        Id = entry.Id,
        PermitId = entry.PermitId,
        AddedInVersion = entry.AddedInVersion,
        RemovedInVersion = entry.RemovedInVersion,
        FileName = entry.FileName,
        SizeBytes = entry.SizeBytes,
        MediaType = entry.MediaType,
        Sha256 = entry.Sha256,
        StorageKey = entry.StorageKey,
        ScanStatus = entry.ScanStatus,
        UploadedBy = entry.UploadedBy,
        UploadedAt = entry.UploadedAt
    };

    private static PermitAttachmentEntry ToEntry(PermitAttachmentRecord record) => new(
        record.Id,
        record.PermitId,
        record.AddedInVersion,
        record.RemovedInVersion,
        record.FileName,
        record.SizeBytes,
        record.MediaType,
        record.Sha256,
        record.StorageKey,
        record.ScanStatus,
        record.UploadedBy,
        record.UploadedAt);

    private static string EncodeETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    private static byte[] DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidRequestException(
                "concurrency.if_match_required",
                "Header If-Match wajib untuk perubahan PTW.");
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
