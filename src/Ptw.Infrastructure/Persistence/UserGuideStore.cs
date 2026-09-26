using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;

namespace Ptw.Infrastructure.Persistence;

public sealed class UserGuideStore(PtwDbContext dbContext, IClock clock) : IUserGuideStore
{
    internal static readonly Guid SettingId = new("ad858db6-2994-4ae3-a7a7-bc7095be3045");

    public async Task<StoredUserGuide?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.UserGuideSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == SettingId, cancellationToken);
        if (setting is null)
        {
            return null;
        }

        var version = await dbContext.UserGuideVersions.AsNoTracking()
            .SingleAsync(x => x.Id == setting.CurrentVersionId, cancellationToken);
        return ToStored(version, setting.Version, setting.UpdatedAt, setting.UpdatedBy);
    }

    public async Task<StoredUserGuide?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.UserGuideCommandReceipts.AsNoTracking()
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

        var version = await dbContext.UserGuideVersions.AsNoTracking()
            .SingleAsync(x => x.Id == receipt.ResultVersionId, cancellationToken);
        return ToStored(
            version,
            receipt.ResultSettingVersion,
            version.UploadedAt,
            version.UploadedBy);
    }

    public async Task<StoredUserGuide> ReplaceAsync(
        StoredUserGuide replacement,
        string expectedETag,
        Actor actor,
        string correlationId,
        UserGuideCommandContext command,
        CancellationToken cancellationToken)
    {
        var expectedVersion = DecodeETag(expectedETag);
        var setting = await dbContext.UserGuideSettings
            .SingleOrDefaultAsync(x => x.Id == SettingId, cancellationToken);
        var currentVersion = setting?.Version ?? 0;
        if (currentVersion != expectedVersion)
        {
            throw new ConcurrencyConflictException();
        }

        var now = clock.UtcNow;
        var nextVersion = currentVersion + 1;
        var version = new UserGuideVersionRecord
        {
            Id = replacement.Id!.Value,
            Version = nextVersion,
            FileName = replacement.FileName,
            SizeBytes = replacement.SizeBytes,
            Sha256 = replacement.Sha256,
            StorageKey = replacement.StorageKey!,
            ScanEvidenceReference = replacement.ScanEvidenceReference!,
            ScannedAt = replacement.ScannedAt!.Value,
            UploadedAt = now,
            UploadedBy = actor.Id
        };
        dbContext.UserGuideVersions.Add(version);
        if (setting is null)
        {
            setting = new UserGuideSettingRecord
            {
                Id = SettingId,
                CurrentVersionId = version.Id,
                Version = nextVersion,
                UpdatedAt = now,
                UpdatedBy = actor.Id
            };
            dbContext.UserGuideSettings.Add(setting);
        }
        else
        {
            setting.CurrentVersionId = version.Id;
            setting.Version = nextVersion;
            setting.UpdatedAt = now;
            setting.UpdatedBy = actor.Id;
        }

        var eventId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new
        {
            UserGuideVersionId = version.Id,
            version.Version,
            version.FileName,
            version.SizeBytes,
            version.Sha256,
            UpdatedBy = actor.Id
        });
        dbContext.ConfigurationAuditEvents.Add(new ConfigurationAuditEventRecord
        {
            Id = eventId,
            AggregateType = "UserGuideSetting",
            AggregateId = SettingId,
            EventType = "user_guide_replaced",
            ActorId = actor.Id,
            OccurredAt = now,
            PayloadJson = payload,
            CorrelationId = correlationId
        });
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = eventId,
            AggregateId = SettingId,
            EventType = "user_guide_replaced",
            PayloadJson = payload,
            OccurredAt = now
        });
        dbContext.UserGuideCommandReceipts.Add(new UserGuideCommandReceiptRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = command.ActorId,
            Operation = command.Operation,
            Key = command.Key,
            RequestHash = command.RequestHash,
            ResultVersionId = version.Id,
            ResultSettingVersion = nextVersion,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24)
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException() { Source = exception.Source };
        }
        catch (DbUpdateException exception)
        {
            throw new ConcurrencyConflictException() { Source = exception.Source };
        }

        return ToStored(version, nextVersion, now, actor.Id);
    }

    private static StoredUserGuide ToStored(
        UserGuideVersionRecord record,
        int settingVersion,
        DateTimeOffset updatedAt,
        string updatedBy) => new(
            record.Id,
            record.FileName,
            record.SizeBytes,
            record.Sha256,
            record.StorageKey,
            record.ScanEvidenceReference,
            record.ScannedAt,
            record.Version,
            updatedAt,
            updatedBy,
            EncodeETag(settingVersion));

    private static string EncodeETag(int version) => $"\"{version}\"";

    private static int DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag)
            || !int.TryParse(etag.Trim().Trim('"'), out var version)
            || version < 0)
        {
            throw new InvalidRequestException(
                "concurrency.if_match_required",
                "Header If-Match wajib dan harus memakai ETag panduan pengguna terbaru.");
        }
        return version;
    }
}
