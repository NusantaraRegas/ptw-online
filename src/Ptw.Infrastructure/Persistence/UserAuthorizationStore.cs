using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Persistence;

public sealed class UserAuthorizationStore(PtwDbContext dbContext) : IUserAuthorizationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StoredUserAuthorization>> ListAsync(CancellationToken cancellationToken)
    {
        var records = await dbContext.UserAuthorizations.AsNoTracking()
            .OrderBy(x => x.SubjectId)
            .ThenBy(x => x.RoleCode)
            .ThenByDescending(x => x.EffectiveFrom)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return records.Select(ToStored).ToArray();
    }

    public async Task<IReadOnlyList<StoredUserAuthorization>> ListApprovedForSubjectAsync(
        string subjectId,
        CancellationToken cancellationToken)
    {
        var records = await dbContext.UserAuthorizations.AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.Status == AuthorizationAssignmentStatus.Approved.ToString())
            .ToListAsync(cancellationToken);
        return records.Select(ToStored).ToArray();
    }

    public async Task<StoredUserAuthorization?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.UserAuthorizations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return record is null ? null : ToStored(record);
    }

    public async Task<StoredUserAuthorization> AddAsync(
        UserAuthorizationAssignment entry,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var record = ToRecord(entry);
        dbContext.UserAuthorizations.Add(record);
        AddVersion(entry, actor.Id);
        AddEvents(entry.DequeueEvents(), actor.Id, correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(record);
    }

    public async Task<StoredUserAuthorization> UpdateAsync(
        UserAuthorizationAssignment entry,
        string expectedETag,
        Actor actor,
        string correlationId,
        AuthorizationCommandContext? command,
        CancellationToken cancellationToken)
    {
        var expectedVersion = DecodeETag(expectedETag);
        var record = await dbContext.UserAuthorizations.SingleAsync(x => x.Id == entry.Id, cancellationToken);
        if (record.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException();
        }

        Apply(record, entry);
        AddVersion(entry, actor.Id);
        AddEvents(entry.DequeueEvents(), actor.Id, correlationId);
        if (command is not null)
        {
            dbContext.AuthorizationCommandReceipts.Add(new AuthorizationCommandReceiptRecord
            {
                Id = Guid.CreateVersion7(),
                ActorId = command.ActorId,
                Operation = command.Operation,
                Key = command.Key,
                RequestHash = command.RequestHash,
                UserAuthorizationId = entry.Id,
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

    public async Task<StoredUserAuthorization?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.AuthorizationCommandReceipts.AsNoTracking()
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

        var version = await dbContext.UserAuthorizationVersions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.UserAuthorizationId == receipt.UserAuthorizationId && x.Version == receipt.ResultVersion,
                cancellationToken)
            ?? throw new InvalidOperationException("Snapshot hasil command assignment tidak ditemukan.");
        var snapshot = JsonSerializer.Deserialize<AuthorizationSnapshot>(version.ContentJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot hasil command assignment tidak valid.");
        return ToStored(snapshot);
    }

    private void AddVersion(UserAuthorizationAssignment entry, string actorId)
    {
        var json = JsonSerializer.Serialize(ToSnapshot(entry), JsonOptions);
        dbContext.UserAuthorizationVersions.Add(new UserAuthorizationVersionRecord
        {
            Id = Guid.CreateVersion7(),
            UserAuthorizationId = entry.Id,
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
                AggregateType = "UserAuthorization",
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

    private static UserAuthorizationRecord ToRecord(UserAuthorizationAssignment entry) => new()
    {
        Id = entry.Id,
        SubjectId = entry.SubjectId,
        RoleCode = entry.RoleCode,
        ActionCodesJson = JsonSerializer.Serialize(entry.ActionCodes, JsonOptions),
        LocationId = entry.LocationId,
        IncludeDescendants = entry.IncludeDescendants,
        RequiredCompetencyCodesJson = JsonSerializer.Serialize(entry.RequiredCompetencyCodes, JsonOptions),
        Kind = entry.Kind.ToString(),
        SourceAuthorizationId = entry.SourceAuthorizationId,
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

    private static void Apply(UserAuthorizationRecord record, UserAuthorizationAssignment entry)
    {
        record.SubjectId = entry.SubjectId;
        record.RoleCode = entry.RoleCode;
        record.ActionCodesJson = JsonSerializer.Serialize(entry.ActionCodes, JsonOptions);
        record.LocationId = entry.LocationId;
        record.IncludeDescendants = entry.IncludeDescendants;
        record.RequiredCompetencyCodesJson = JsonSerializer.Serialize(entry.RequiredCompetencyCodes, JsonOptions);
        record.Kind = entry.Kind.ToString();
        record.SourceAuthorizationId = entry.SourceAuthorizationId;
        record.EffectiveFrom = entry.EffectiveFrom;
        record.EffectiveUntil = entry.EffectiveUntil;
        record.Status = entry.Status.ToString();
        record.Version = entry.Version;
        record.MakerId = entry.MakerId;
        record.CheckerId = entry.CheckerId;
        record.ApprovedAt = entry.ApprovedAt;
        record.UpdatedAt = entry.UpdatedAt;
    }

    private static StoredUserAuthorization ToStored(UserAuthorizationRecord record) => new(
        UserAuthorizationAssignment.Rehydrate(
            record.Id,
            record.SubjectId,
            record.RoleCode,
            DeserializeCodes(record.ActionCodesJson),
            record.LocationId,
            record.IncludeDescendants,
            DeserializeCodes(record.RequiredCompetencyCodesJson),
            Enum.Parse<AuthorizationAssignmentKind>(record.Kind),
            record.SourceAuthorizationId,
            record.EffectiveFrom,
            record.EffectiveUntil,
            Enum.Parse<AuthorizationAssignmentStatus>(record.Status),
            record.Version,
            record.MakerId,
            record.CheckerId,
            record.ApprovedAt,
            record.CreatedAt,
            record.UpdatedAt),
        EncodeETag(record.Version));

    private static AuthorizationSnapshot ToSnapshot(UserAuthorizationAssignment entry) => new(
        entry.Id,
        entry.SubjectId,
        entry.RoleCode,
        entry.ActionCodes,
        entry.LocationId,
        entry.IncludeDescendants,
        entry.RequiredCompetencyCodes,
        entry.Kind,
        entry.SourceAuthorizationId,
        entry.EffectiveFrom,
        entry.EffectiveUntil,
        entry.Status,
        entry.Version,
        entry.MakerId,
        entry.CheckerId,
        entry.ApprovedAt,
        entry.CreatedAt,
        entry.UpdatedAt);

    private static StoredUserAuthorization ToStored(AuthorizationSnapshot snapshot) => new(
        UserAuthorizationAssignment.Rehydrate(
            snapshot.Id,
            snapshot.SubjectId,
            snapshot.RoleCode,
            snapshot.ActionCodes,
            snapshot.LocationId,
            snapshot.IncludeDescendants,
            snapshot.RequiredCompetencyCodes,
            snapshot.Kind,
            snapshot.SourceAuthorizationId,
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

    private static string[] DeserializeCodes(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions)
        ?? throw new InvalidOperationException("Daftar code assignment tidak valid.");

    private static string EncodeETag(int version) => $"\"authorization-v{version}\"";

    private static int DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidRequestException(
                "concurrency.if_match_required",
                "Header If-Match wajib untuk perubahan assignment otorisasi.");
        }

        try
        {
            var value = etag.Trim().Trim('"');
            return value.StartsWith("authorization-v", StringComparison.Ordinal)
                && int.TryParse(value[15..], out var version)
                && version > 0
                    ? version
                    : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidRequestException("concurrency.invalid_etag", "Format ETag tidak valid.");
        }
    }

    private sealed record AuthorizationSnapshot(
        Guid Id,
        string SubjectId,
        string RoleCode,
        IReadOnlyList<string> ActionCodes,
        Guid? LocationId,
        bool IncludeDescendants,
        IReadOnlyList<string> RequiredCompetencyCodes,
        AuthorizationAssignmentKind Kind,
        Guid? SourceAuthorizationId,
        DateTimeOffset EffectiveFrom,
        DateTimeOffset? EffectiveUntil,
        AuthorizationAssignmentStatus Status,
        int Version,
        string MakerId,
        string? CheckerId,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
