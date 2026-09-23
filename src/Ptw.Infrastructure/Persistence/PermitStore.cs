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
        if (record is null)
        {
            return null;
        }

        var renewalPermitId = await dbContext.Permits.AsNoTracking()
            .Where(x => x.RenewedFromPermitId == id)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return ToStored(record, renewalPermitId);
    }

    public async Task<IReadOnlyList<StoredPermit>> ListAsync(string? sponsorId, CancellationToken cancellationToken)
    {
        var query = dbContext.Permits.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sponsorId))
        {
            query = query.Where(x => x.SponsorId == sponsorId);
        }

        var records = await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        var recordIds = records.Select(x => x.Id).ToArray();
        var renewals = await dbContext.Permits.AsNoTracking()
            .Where(x => x.RenewedFromPermitId != null && recordIds.Contains(x.RenewedFromPermitId.Value))
            .Select(x => new { SourceId = x.RenewedFromPermitId!.Value, RenewalId = x.Id })
            .ToDictionaryAsync(x => x.SourceId, x => x.RenewalId, cancellationToken);
        return records.Select(record => ToStored(
            record,
            renewals.GetValueOrDefault(record.Id))).ToArray();
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
        AddRevision(permit, actor.Id);
        var events = permit.DequeueEvents();
        AddEvents(events, actor.Id, correlationId);
        AddAuthorizationEvidence(permit.Id, actor.Id, correlationId, authorizationEvidence);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(record);
    }

    public async Task<StoredPermitRenewal> AddRenewalAsync(
        Permit source,
        Permit renewal,
        string expectedSourceETag,
        Actor actor,
        string correlationId,
        IdempotencyContext idempotency,
        PolicyAuthorizationEvidence? authorizationEvidence,
        CancellationToken cancellationToken)
    {
        var persistedSourceVersion = await dbContext.Permits.AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.BusinessVersion)
            .SingleAsync(cancellationToken);
        var sourceRecord = ToRecord(source);
        sourceRecord.RowVersion = DecodeETag(expectedSourceETag);
        dbContext.Permits.Attach(sourceRecord);
        dbContext.Entry(sourceRecord).State = EntityState.Modified;
        dbContext.Entry(sourceRecord).Property(x => x.RowVersion).OriginalValue = sourceRecord.RowVersion;

        var renewalRecord = ToRecord(renewal);
        dbContext.Permits.Add(renewalRecord);
        if (source.Version > persistedSourceVersion)
        {
            AddRevision(source, actor.Id);
        }
        AddRevision(renewal, actor.Id);
        var sourceEvents = source.DequeueEvents();
        AddEvents(sourceEvents, actor.Id, correlationId);
        AddEvents(renewal.DequeueEvents(), actor.Id, correlationId);
        await ApplyWorkflowTasksAsync(source, sourceEvents, actor.Id, cancellationToken);
        AddAuthorizationEvidence(source.Id, actor.Id, correlationId, authorizationEvidence);
        dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = idempotency.ActorId,
            Operation = idempotency.Operation,
            Key = idempotency.Key,
            RequestHash = idempotency.RequestHash,
            PermitId = renewal.Id,
            CreatedAt = source.UpdatedAt,
            ExpiresAt = source.UpdatedAt.AddHours(24)
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException() { Source = exception.Source };
        }

        return new StoredPermitRenewal(
            ToStored(sourceRecord, renewal.Id),
            ToStored(renewalRecord));
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
            .Select(x => x.BusinessVersion)
            .SingleAsync(cancellationToken);
        var record = ToRecord(permit);
        record.RowVersion = DecodeETag(expectedETag);
        dbContext.Permits.Attach(record);
        dbContext.Entry(record).State = EntityState.Modified;
        dbContext.Entry(record).Property(x => x.RowVersion).OriginalValue = record.RowVersion;
        if (permit.Version > persistedVersion)
        {
            AddRevision(permit, actor.Id);
        }
        else if (permit.Status == PermitStatus.Draft)
        {
            await UpdateWorkingRevisionAsync(permit, actor.Id, cancellationToken);
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
        var query = dbContext.PermitRevisions.AsNoTracking().Where(x => x.PermitId == permitId);
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
        var query =
            from task in dbContext.PermitTasks.AsNoTracking()
            join permit in dbContext.Permits.AsNoTracking() on task.PermitId equals permit.Id
            where task.Status == "PENDING"
                && (task.AssignedActorId == actorId
                    || (task.AssignedActorId == null
                        && (roleCodes.Contains(task.RequiredRole)
                            || (task.Type == "AREA_CLOSE_VERIFICATION"
                                && roleCodes.Contains("AreaOwnerSeniorOfficer")))))
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
                x.Task.BusinessPermitVersion,
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

    public async Task<PermitTaskEntry?> FindPendingTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var record = await (
            from task in dbContext.PermitTasks.AsNoTracking()
            join permit in dbContext.Permits.AsNoTracking() on task.PermitId equals permit.Id
            where task.Id == taskId && task.Status == "PENDING"
            select new { Task = task, Permit = permit })
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }

        var draft = JsonSerializer.Deserialize<PermitDraft>(record.Permit.DraftJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot draft PTW tidak valid.");
        return new PermitTaskEntry(
            record.Task.Id,
            record.Task.PermitId,
            record.Task.BusinessPermitVersion,
            record.Task.Type,
            record.Task.Label,
            record.Task.RequiredRole,
            record.Task.Status,
            record.Permit.PermitNumber,
            draft.Title,
            record.Permit.LocationId,
            record.Task.CreatedAt,
            record.Task.CompletedAt);
    }

    public async Task<PrintPackageEntry?> FindPrintPackageAsync(
        Guid printPackageId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.PrintPackageSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == printPackageId, cancellationToken);
        return record is null
            ? null
            : new PrintPackageEntry(
                record.Id,
                record.PermitId,
                record.BusinessPermitVersion,
                record.PermitVersion,
                record.RenderStatus);
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
                case "permit_submitted":
                    await CompletePendingTaskIfExistsAsync(
                        permit.Id,
                        "SPONSOR_REVISION",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    AddTask(permit, "HSE_VALIDATION", "Validasi PIC HSE", "HSEValidator", domainEvent.OccurredAt);
                    break;
                case "permit_draft_updated":
                case "permit_attachment_added":
                case "permit_attachment_removed":
                    await RefreshPendingTaskVersionIfExistsAsync(
                        permit.Id,
                        permit.ChangeSequence,
                        permit.Version,
                        "SPONSOR_REVISION",
                        cancellationToken);
                    break;
                case "hse_validation_completed":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "HSE_VALIDATION",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    AddTask(
                        permit,
                        "AREA_OPERATION_REVIEW",
                        "Verifikasi kondisi operasi Bagian 7",
                        "AreaOwnerSeniorOfficer",
                        domainEvent.OccurredAt);
                    break;
                case "area_operations_review_completed":
                    await AddAreaOperationsReviewDecisionAsync(
                        permit,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_OPERATION_REVIEW",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    AddTask(
                        permit,
                        "AREA_APPROVE_AND_ISSUE",
                        "Setujui dan terbitkan PTW",
                        "AreaOwnerManager",
                        domainEvent.OccurredAt);
                    break;
                case "permit_issued":
                    await AddIssuanceArtifactsAsync(permit, domainEvent.OccurredAt, cancellationToken);
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_APPROVE_AND_ISSUE",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "closure_requested":
                    AddTask(
                        permit,
                        "AREA_CLOSE_VERIFICATION",
                        "Verifikasi hardcopy dan tutup PTW",
                        "AreaOwnerManager",
                        domainEvent.OccurredAt);
                    break;
                case "closure_resubmitted":
                    await RefreshPendingTaskVersionAsync(
                        permit.Id,
                        permit.ChangeSequence,
                        permit.Version,
                        "AREA_CLOSE_VERIFICATION",
                        cancellationToken);
                    break;
                case "permit_renewal_requested":
                    AddTask(
                        permit,
                        "AREA_RENEWAL_REVIEW",
                        "Review perpanjangan PTW",
                        "AreaOwnerManager",
                        domainEvent.OccurredAt);
                    break;
                case "renewal_evidence_replacement_requested":
                case "renewal_rejected":
                case "renewal_approved":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_RENEWAL_REVIEW",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "permit_closed":
                    await CompleteTaskAsync(
                        permit.Id,
                        permit.Version,
                        "AREA_CLOSE_VERIFICATION",
                        actorId,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
                case "revision_requested":
                    await CancelPendingTasksAsync(
                        permit.Id,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    AddTask(
                        permit,
                        "SPONSOR_REVISION",
                        "Perbaiki dan ajukan ulang PTW",
                        "Sponsor",
                        domainEvent.OccurredAt,
                        permit.Draft.SponsorId);
                    break;
                case "permit_rejected":
                case "permit_cancelled":
                    await CancelPendingTasksAsync(
                        permit.Id,
                        domainEvent.OccurredAt,
                        cancellationToken);
                    break;
            }
        }
    }

    private async Task AddIssuanceArtifactsAsync(
        Permit permit,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var approval = permit.Approval ?? throw new InvalidOperationException(
            "Bukti approval wajib tersedia ketika PTW diterbitkan.");
        var taskId = await dbContext.PermitTasks
            .Where(x => x.PermitId == permit.Id
                && x.BusinessPermitVersion == permit.Version
                && x.Type == "AREA_APPROVE_AND_ISSUE"
                && x.Status == "PENDING")
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        var evidenceJson = JsonSerializer.Serialize(approval, JsonOptions);
        var decisionId = Guid.CreateVersion7();
        dbContext.PermitDecisions.Add(new PermitDecisionRecord
        {
            Id = decisionId,
            PermitId = permit.Id,
            PermitVersion = permit.ChangeSequence,
            BusinessPermitVersion = permit.Version,
            TaskId = taskId,
            Decision = "APPROVE_AND_ISSUE",
            ActorId = approval.ActorId,
            ActorPosition = approval.ActorPosition,
            ApprovalCapacity = ToUpperSnakeCase(approval.Capacity.ToString()),
            PrincipalManagerUserId = approval.PrincipalManagerUserId,
            PrincipalPosition = approval.PrincipalPosition,
            AuthorizationId = approval.AuthorizationId,
            ActingAssignmentId = approval.ActingAssignmentId,
            Statement = approval.Statement,
            DecidedAt = approval.ApprovedAt,
            EvidenceHash = Hash(evidenceJson)
        });

        var supportingDocuments = (await dbContext.PermitAttachments.AsNoTracking()
                .Where(x => x.PermitId == permit.Id
                    && x.RemovedInVersion == null
                    && (x.SupportingDocumentCode != null || x.Category == "JSA"))
                .OrderBy(x => x.UploadedAt)
                .ToListAsync(cancellationToken))
            .Select(attachment => new SupportingDocumentEvidenceSnapshot(
                attachment.Id,
                attachment.SupportingDocumentCode ?? PermitSupportingDocumentCatalog.JsaCode,
                attachment.FileName,
                attachment.Sha256,
                attachment.ScanStatus,
                attachment.DocumentNumber,
                attachment.DocumentRevision,
                attachment.DocumentDate))
            .ToArray();
        var sponsor = await ResolveSponsorPrintEvidenceAsync(permit, cancellationToken);

        var snapshotJson = JsonSerializer.Serialize(new
        {
            permit.Id,
            permit.PermitNumber,
            PermitVersion = permit.Version,
            Status = "ISSUED",
            Permit = permit.Draft,
            HseValidation = permit.HseValidation,
            AreaOperationsReview = permit.AreaOperationsReview,
            Approval = approval,
            approval.RuleVersion,
            approval.PrintTemplateVersion,
            approval.CampaignAssetVersion,
            CreatedAt = occurredAt,
            SupportingDocuments = supportingDocuments,
            Sponsor = sponsor
        }, JsonOptions);
        var snapshotId = Guid.CreateVersion7();
        dbContext.PrintPackageSnapshots.Add(new PrintPackageSnapshotRecord
        {
            Id = snapshotId,
            PermitId = permit.Id,
            PermitVersion = permit.ChangeSequence,
            BusinessPermitVersion = permit.Version,
            DecisionId = decisionId,
            RuleVersion = approval.RuleVersion,
            PrintTemplateVersion = approval.PrintTemplateVersion,
            CampaignAssetVersion = approval.CampaignAssetVersion,
            SnapshotJson = snapshotJson,
            SnapshotHash = Hash(snapshotJson),
            RenderStatus = "PENDING",
            CreatedAt = occurredAt
        });
        dbContext.GeneratedDocuments.Add(new GeneratedDocumentRecord
        {
            Id = Guid.CreateVersion7(),
            PrintPackageSnapshotId = snapshotId,
            RenderStatus = "PENDING"
        });
    }

    private async Task<SponsorPrintEvidence> ResolveSponsorPrintEvidenceAsync(
        Permit permit,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == permit.Draft.SponsorId, cancellationToken);
        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.SubjectId == permit.Draft.SponsorId && x.IsActive,
                cancellationToken);
        var submittedAt = await dbContext.AuditEvents.AsNoTracking()
            .Where(x => x.PermitId == permit.Id && x.EventType == "permit_submitted")
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => (DateTimeOffset?)x.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? permit.UpdatedAt;

        return new SponsorPrintEvidence(
            permit.Draft.SponsorId,
            account?.DisplayName ?? permit.Draft.SponsorId,
            account?.Position,
            account?.Department,
            submittedAt,
            signature is null
                ? null
                : new VisualSignatureEvidence(
                    signature.Id,
                    signature.Version,
                    signature.MediaType,
                    signature.Content,
                    signature.Sha256));
    }

    private async Task AddAreaOperationsReviewDecisionAsync(
        Permit permit,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var review = permit.AreaOperationsReview ?? throw new InvalidOperationException(
            "Bukti review SO/Officer Pemilik Wilayah wajib tersedia ketika task Bagian 7 diselesaikan.");
        var taskId = await dbContext.PermitTasks
            .Where(x => x.PermitId == permit.Id
                && x.BusinessPermitVersion == permit.Version
                && x.Type == "AREA_OPERATION_REVIEW"
                && x.Status == "PENDING")
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);
        var evidenceJson = JsonSerializer.Serialize(review, JsonOptions);
        dbContext.PermitDecisions.Add(new PermitDecisionRecord
        {
            Id = Guid.CreateVersion7(),
            PermitId = permit.Id,
            PermitVersion = permit.ChangeSequence,
            BusinessPermitVersion = permit.Version,
            TaskId = taskId,
            Decision = "AREA_OPERATION_REVIEW",
            ActorId = review.ActorId,
            ActorPosition = review.ActorPosition,
            ApprovalCapacity = "AREA_OPERATIONS_REVIEWER",
            PrincipalManagerUserId = review.ActorId,
            PrincipalPosition = review.ActorPosition,
            AuthorizationId = review.AuthorizationId,
            Statement = review.Statement,
            DecidedAt = occurredAt,
            EvidenceHash = Hash(evidenceJson)
        });
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
            PermitVersion = permit.ChangeSequence,
            BusinessPermitVersion = permit.Version,
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
                && x.BusinessPermitVersion == permitVersion
                && x.Type == type
                && x.Status == "PENDING",
            cancellationToken);
        task.Status = "COMPLETED";
        task.CompletedAt = completedAt;
        task.CompletedBy = actorId;
    }

    private async Task RefreshPendingTaskVersionAsync(
        Guid permitId,
        int changeSequence,
        int permitVersion,
        string type,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.PermitTasks.SingleAsync(
            x => x.PermitId == permitId
                && x.Type == type
                && x.Status == "PENDING",
            cancellationToken);
        task.PermitVersion = changeSequence;
        task.BusinessPermitVersion = permitVersion;
    }

    private async Task RefreshPendingTaskVersionIfExistsAsync(
        Guid permitId,
        int changeSequence,
        int permitVersion,
        string type,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.PermitTasks.SingleOrDefaultAsync(
            x => x.PermitId == permitId
                && x.Type == type
                && x.Status == "PENDING",
            cancellationToken);
        if (task is not null)
        {
            task.PermitVersion = changeSequence;
            task.BusinessPermitVersion = permitVersion;
        }
    }

    private async Task CompletePendingTaskIfExistsAsync(
        Guid permitId,
        string type,
        string actorId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.PermitTasks.SingleOrDefaultAsync(
            x => x.PermitId == permitId
                && x.Type == type
                && x.Status == "PENDING",
            cancellationToken);
        if (task is null)
        {
            return;
        }

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

    private void AddRevision(Permit permit, string actorId)
    {
        var json = JsonSerializer.Serialize(permit.Draft, JsonOptions);
        dbContext.PermitRevisions.Add(new PermitRevisionRecord
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

    private async Task UpdateWorkingRevisionAsync(
        Permit permit,
        string actorId,
        CancellationToken cancellationToken)
    {
        var revision = await dbContext.PermitRevisions.SingleAsync(
            x => x.PermitId == permit.Id && x.Version == permit.Version,
            cancellationToken);
        var json = JsonSerializer.Serialize(permit.Draft, JsonOptions);
        revision.ContentJson = json;
        revision.ContentHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        revision.CreatedAt = permit.UpdatedAt;
        revision.CreatedBy = actorId;
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
        Version = permit.ChangeSequence,
        BusinessVersion = permit.Version,
        LocationId = permit.Draft.LocationId,
        SponsorId = permit.Draft.SponsorId,
        ValidFrom = permit.Draft.ValidFrom,
        ValidUntil = permit.Draft.ValidUntil,
        DraftJson = JsonSerializer.Serialize(permit.Draft, JsonOptions),
        CreatedAt = permit.CreatedAt,
        UpdatedAt = permit.UpdatedAt,
        RenewedFromPermitId = permit.RenewedFromPermitId,
        SuspensionReason = permit.SuspensionReason,
        WorkflowEvidenceJson = JsonSerializer.Serialize(
            new PermitWorkflowSnapshot(
                permit.HseValidation,
                permit.AreaOperationsReview,
                permit.Approval,
                permit.Suspension,
                permit.ClosureRequest,
                permit.ClosureDecision,
                permit.RenewalRequest),
            JsonOptions)
    };

    private static StoredPermit ToStored(PermitRecord record, Guid? renewalPermitId = null)
    {
        var draft = JsonSerializer.Deserialize<PermitDraft>(record.DraftJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot draft PTW tidak valid.");
        var workflow = string.IsNullOrWhiteSpace(record.WorkflowEvidenceJson)
            ? new PermitWorkflowSnapshot(null, null, null, null, null, null, null)
            : JsonSerializer.Deserialize<PermitWorkflowSnapshot>(record.WorkflowEvidenceJson, JsonOptions)
                ?? throw new InvalidOperationException("Bukti workflow PTW tidak valid.");
        var permit = Permit.Rehydrate(
            record.Id,
            record.PermitNumber,
            Enum.Parse<PermitStatus>(record.Status),
            record.BusinessVersion,
            draft,
            record.CreatedAt,
            record.UpdatedAt,
            record.SuspensionReason,
            workflow.HseValidation,
            workflow.Approval,
            workflow.Suspension,
            workflow.ClosureRequest,
            workflow.ClosureDecision,
            workflow.RenewalRequest,
            record.RenewedFromPermitId,
            renewalPermitId,
            workflow.AreaOperationsReview,
            record.Version);
        return new StoredPermit(permit, EncodeETag(record.RowVersion));
    }

    private sealed record PermitWorkflowSnapshot(
        PermitValidationEvidence? HseValidation,
        AreaOperationsReviewEvidence? AreaOperationsReview,
        PermitApprovalEvidence? Approval,
        PermitSuspensionEvidence? Suspension = null,
        PermitClosureEvidence? ClosureRequest = null,
        PermitClosureDecisionEvidence? ClosureDecision = null,
        PermitRenewalRequestEvidence? RenewalRequest = null);

    private static string EncodeETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static string ToUpperSnakeCase(string value) => string.Concat(
        value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{character}" : character.ToString()))
        .ToUpperInvariant();

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
