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

    public async Task<StoredPermit> AddAsync(
        Permit permit,
        Actor actor,
        string correlationId,
        PolicyAuthorizationEvidence? authorizationEvidence,
        CancellationToken cancellationToken)
    {
        var record = ToRecord(permit);
        dbContext.Permits.Add(record);
        AddVersion(permit, actor.Id);
        var events = permit.DequeueEvents();
        AddEvents(events, actor.Id, correlationId);
        AddAuthorizationEvidence(permit.Id, actor.Id, correlationId, authorizationEvidence);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(record);
    }

    public async Task<StoredPermit> UpdateAsync(
        Permit permit,
        string expectedETag,
        Actor actor,
        string correlationId,
        IdempotencyContext? idempotency,
        PolicyAuthorizationEvidence? authorizationEvidence,
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
        var events = permit.DequeueEvents();
        AddEvents(events, actor.Id, correlationId);
        await ApplyWorkflowTasksAsync(permit, events, actor.Id, cancellationToken);
        AddAuthorizationEvidence(permit.Id, actor.Id, correlationId, authorizationEvidence);
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

    public async Task<StorePage<PermitActivityEntry>> ListActivityAsync(
        Guid permitId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEvents.AsNoTracking().Where(x => x.PermitId == permitId);
        var count = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(x => x.Sequence)
            .Skip(offset)
            .Take(limit)
            .Select(x => new PermitActivityEntry(
                x.Sequence,
                x.EventType,
                x.ActorId,
                x.OccurredAt,
                x.PayloadJson,
                x.CorrelationId))
            .ToListAsync(cancellationToken);
        return new StorePage<PermitActivityEntry>(records, count);
    }

    public async Task<StorePage<PermitVersionEntry>> ListVersionsAsync(
        Guid permitId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PermitVersions.AsNoTracking().Where(x => x.PermitId == permitId);
        var count = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(x => x.Version)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var entries = records.Select(record => new PermitVersionEntry(
            record.Version,
            JsonSerializer.Deserialize<PermitDraft>(record.ContentJson, JsonOptions)
                ?? throw new InvalidOperationException("Snapshot versi PTW tidak valid."),
            record.ContentHash,
            record.CreatedAt,
            record.CreatedBy)).ToArray();
        return new StorePage<PermitVersionEntry>(entries, count);
    }

    public async Task<IReadOnlyList<PermitTaskEntry>> ListPendingTasksAsync(
        string actorId,
        IReadOnlySet<string> roles,
        IReadOnlySet<string> locationScopes,
        CancellationToken cancellationToken)
    {
        var roleCodes = roles.ToArray();
        if (roleCodes.Length == 0)
        {
            return [];
        }

        var query =
            from task in dbContext.PermitTasks.AsNoTracking()
            join permit in dbContext.Permits.AsNoTracking() on task.PermitId equals permit.Id
            where task.Status == "PENDING"
                && roleCodes.Contains(task.RequiredRole)
                && (task.AssignedActorId == null || task.AssignedActorId == actorId)
            select new { Task = task, Permit = permit };

        if (!locationScopes.Contains("*"))
        {
            var scopes = locationScopes.ToArray();
            query = query.Where(x => scopes.Contains(x.Permit.LocationId));
        }

        var records = await query
            .OrderBy(x => x.Task.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return records.Select(x =>
        {
            var draft = JsonSerializer.Deserialize<PermitDraft>(x.Permit.DraftJson, JsonOptions)
                ?? throw new InvalidOperationException("Snapshot draft PTW tidak valid.");
            return new PermitTaskEntry(
                x.Task.Id,
                x.Task.PermitId,
                x.Task.PermitVersion,
                x.Task.Type,
                x.Task.Label,
                x.Task.RequiredRole,
                x.Task.Status,
                x.Permit.PermitNumber,
                draft.Title,
                x.Permit.LocationId,
                x.Task.CreatedAt,
                x.Task.CompletedAt);
        }).ToArray();
    }

    private async Task ApplyWorkflowTasksAsync(
        Permit permit,
        IReadOnlyList<DomainEvent> events,
        string actorId,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in events)
        {
            switch (domainEvent.Type)
            {
                case "review_started":
                    AddTask(permit, "HSSE_VALIDATION", "Validasi HSSE", "HSSEValidator", domainEvent.OccurredAt);
                    break;
                case "permit_validation_endorsed":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "HSSE_VALIDATION",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "hsse_validation_completed":
                    AddTask(
                        permit,
                        "AREA_OWNER_APPROVAL",
                        "Persetujuan PIC pemilik area",
                        "AreaOwnerApprover",
                        domainEvent.OccurredAt);
                    break;
                case "permit_approved":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_OWNER_APPROVAL",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    AddTask(
                        permit,
                        "AREA_OWNER_ISSUE",
                        "Penerbitan oleh PIC pemilik area",
                        "AreaOwnerApprover",
                        domainEvent.OccurredAt,
                        actorId);
                    break;
                case "permit_issued":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_OWNER_ISSUE",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "suspension_requested":
                    AddTask(
                        permit,
                        "SUSPENSION_APPROVAL",
                        "Persetujuan penangguhan pekerjaan",
                        "AreaOwnerApprover",
                        domainEvent.OccurredAt);
                    break;
                case "suspension_approved":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "SUSPENSION_APPROVAL",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "completion_declared":
                    AddTask(
                        permit,
                        "HSSE_COMPLETION_CONFIRMATION",
                        "Konfirmasi penyelesaian oleh HSSE",
                        "HSSEValidator",
                        domainEvent.OccurredAt);
                    AddTask(
                        permit,
                        "AREA_OWNER_COMPLETION_CONFIRMATION",
                        "Konfirmasi penyelesaian oleh PIC pemilik area",
                        "AreaOwnerApprover",
                        domainEvent.OccurredAt);
                    break;
                case "completion_confirmed":
                    var payload = JsonSerializer.SerializeToElement(domainEvent.Payload, JsonOptions);
                    var confirmation = payload.GetProperty("confirmation").GetString();
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        confirmation == nameof(PermitCompletionKind.Hsse)
                            ? "HSSE_COMPLETION_CONFIRMATION"
                            : "AREA_OWNER_COMPLETION_CONFIRMATION",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "completion_confirmations_completed":
                    AddTask(
                        permit,
                        "AREA_OWNER_CLOSE",
                        "Penutupan PTW oleh PIC pemilik area",
                        "AreaOwnerApprover",
                        domainEvent.OccurredAt);
                    break;
                case "permit_closed":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_OWNER_CLOSE",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "revision_requested":
                case "permit_rejected":
                    await CancelPendingTasksAsync(
                        permit.Id,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
            }
        }
    }

    private void AddTask(
        Permit permit,
        string type,
        string label,
        string role,
        DateTimeOffset createdAt,
        string? assignedActorId = null)
    {
        dbContext.PermitTasks.Add(new PermitTaskRecord
        {
            Id = Guid.CreateVersion7(),
            PermitId = permit.Id,
            PermitVersion = permit.Version,
            Type = type,
            Label = label,
            RequiredRole = role,
            AssignedActorId = assignedActorId,
            Status = "PENDING",
            CreatedAt = createdAt
        });
    }

    private async Task CompleteTaskAsync(
        Guid permitId,
        int permitVersion,
        string type,
        string actorId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.PermitTasks.SingleAsync(
            x => x.PermitId == permitId
                && x.PermitVersion == permitVersion
                && x.Type == type
                && x.Status == "PENDING",
            cancellationToken);
        task.Status = "COMPLETED";
        task.CompletedAt = completedAt;
        task.CompletedBy = actorId;
    }

    private async Task CancelPendingTasksAsync(
        Guid permitId,
        DateTimeOffset cancelledAt,
        CancellationToken cancellationToken)
    {
        var tasks = await dbContext.PermitTasks
            .Where(x => x.PermitId == permitId && x.Status == "PENDING")
            .ToListAsync(cancellationToken);
        foreach (var task in tasks)
        {
            task.Status = "CANCELLED";
            task.CancelledAt = cancelledAt;
        }
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

    private void AddAuthorizationEvidence(
        Guid permitId,
        string actorId,
        string correlationId,
        PolicyAuthorizationEvidence? evidence)
    {
        if (evidence is null)
        {
            return;
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.CreateVersion7(),
            PermitId = permitId,
            EventType = "PermitAuthorizationEvaluated",
            ActorId = actorId,
            OccurredAt = evidence.EvaluatedAt,
            PayloadJson = JsonSerializer.Serialize(evidence, JsonOptions),
            CorrelationId = correlationId
        });
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
        SuspensionReason = permit.SuspensionReason,
        WorkflowEvidenceJson = JsonSerializer.Serialize(
            new PermitWorkflowSnapshot(
                permit.HsseValidation,
                permit.GasDistributionValidation,
                permit.Approval,
                permit.Suspension,
                permit.SponsorCompletion,
                permit.HsseCompletion,
                permit.AreaOwnerCompletion),
            JsonOptions)
    };

    private static StoredPermit ToStored(PermitRecord record)
    {
        var draft = JsonSerializer.Deserialize<PermitDraft>(record.DraftJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot draft PTW tidak valid.");
        var workflow = string.IsNullOrWhiteSpace(record.WorkflowEvidenceJson)
            ? new PermitWorkflowSnapshot(null, null, null)
            : JsonSerializer.Deserialize<PermitWorkflowSnapshot>(record.WorkflowEvidenceJson, JsonOptions)
                ?? throw new InvalidOperationException("Bukti workflow PTW tidak valid.");
        var permit = Permit.Rehydrate(
            record.Id,
            record.PermitNumber,
            Enum.Parse<PermitStatus>(record.Status),
            record.Version,
            draft,
            record.CreatedAt,
            record.UpdatedAt,
            record.ActiveWorkPeriodId,
            record.SuspensionReason,
            workflow.HsseValidation,
            workflow.GasDistributionValidation,
            workflow.Approval,
            workflow.Suspension,
            workflow.SponsorCompletion,
            workflow.HsseCompletion,
            workflow.AreaOwnerCompletion);
        return new StoredPermit(permit, EncodeETag(record.RowVersion));
    }

    private sealed record PermitWorkflowSnapshot(
        PermitValidationEvidence? HsseValidation,
        PermitValidationEvidence? GasDistributionValidation,
        PermitApprovalEvidence? Approval,
        PermitSuspensionEvidence? Suspension = null,
        PermitCompletionEvidence? SponsorCompletion = null,
        PermitCompletionEvidence? HsseCompletion = null,
        PermitCompletionEvidence? AreaOwnerCompletion = null);

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
