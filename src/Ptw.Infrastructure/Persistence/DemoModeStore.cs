using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;

namespace Ptw.Infrastructure.Persistence;

public sealed class DemoModeStore(
    PtwDbContext dbContext,
    IClock clock,
    DemoModeDefaults defaults) : IDemoModeStore
{
    internal static readonly Guid SettingId = new("7fc26c84-e6ee-4dc9-8c75-b7b246b4612d");

    public async Task<StoredDemoModeSetting> GetAsync(CancellationToken cancellationToken)
    {
        var record = await dbContext.DemoModeSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == SettingId, cancellationToken);
        return record is null ? Default() : ToStored(record);
    }

    public async Task<StoredDemoModeSetting?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.DemoModeCommandReceipts.AsNoTracking()
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

        return new StoredDemoModeSetting(
            receipt.ResultEnabled,
            receipt.ResultVersion,
            receipt.CreatedAt,
            receipt.ActorId,
            EncodeETag(receipt.ResultVersion));
    }

    public async Task<StoredDemoModeSetting> SetAsync(
        bool enabled,
        string expectedETag,
        Actor actor,
        string correlationId,
        DemoModeCommandContext command,
        CancellationToken cancellationToken)
    {
        var expectedVersion = DecodeETag(expectedETag);
        var record = await dbContext.DemoModeSettings
            .SingleOrDefaultAsync(x => x.Id == SettingId, cancellationToken);
        var currentVersion = record?.Version ?? 0;
        if (currentVersion != expectedVersion)
        {
            throw new ConcurrencyConflictException();
        }

        var now = clock.UtcNow;
        if (record is null)
        {
            record = new DemoModeSettingRecord
            {
                Id = SettingId,
                Enabled = enabled,
                Version = 1,
                UpdatedAt = now,
                UpdatedBy = actor.Id
            };
            dbContext.DemoModeSettings.Add(record);
        }
        else
        {
            record.Enabled = enabled;
            record.Version++;
            record.UpdatedAt = now;
            record.UpdatedBy = actor.Id;
        }

        var eventId = Guid.CreateVersion7();
        var eventType = enabled ? "demo_mode_enabled" : "demo_mode_disabled";
        var payload = JsonSerializer.Serialize(new
        {
            Enabled = enabled,
            record.Version,
            UpdatedBy = actor.Id
        });
        dbContext.ConfigurationAuditEvents.Add(new ConfigurationAuditEventRecord
        {
            Id = eventId,
            AggregateType = "DemoModeSetting",
            AggregateId = SettingId,
            EventType = eventType,
            ActorId = actor.Id,
            OccurredAt = now,
            PayloadJson = payload,
            CorrelationId = correlationId
        });
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = eventId,
            AggregateId = SettingId,
            EventType = eventType,
            PayloadJson = payload,
            OccurredAt = now
        });
        dbContext.DemoModeCommandReceipts.Add(new DemoModeCommandReceiptRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = command.ActorId,
            Operation = command.Operation,
            Key = command.Key,
            RequestHash = command.RequestHash,
            ResultEnabled = enabled,
            ResultVersion = record.Version,
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
        catch (DbUpdateException exception) when (record.Version == 1)
        {
            throw new ConcurrencyConflictException() { Source = exception.Source };
        }

        return ToStored(record);
    }

    private StoredDemoModeSetting Default() => new(
        defaults.Enabled,
        0,
        null,
        null,
        EncodeETag(0));

    private static StoredDemoModeSetting ToStored(DemoModeSettingRecord record) => new(
        record.Enabled,
        record.Version,
        record.UpdatedAt,
        record.UpdatedBy,
        EncodeETag(record.Version));

    private static string EncodeETag(int version) => $"\"{version}\"";

    private static int DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag)
            || !int.TryParse(etag.Trim().Trim('"'), out var version)
            || version < 0)
        {
            throw new InvalidRequestException(
                "concurrency.if_match_required",
                "Header If-Match wajib dan harus memakai ETag pengaturan terbaru.");
        }

        return version;
    }
}

public sealed record DemoModeDefaults(bool Enabled);

