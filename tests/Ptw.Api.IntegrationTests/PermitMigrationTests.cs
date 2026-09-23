using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ptw.Infrastructure.Persistence;
using System.Text.Json;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class PermitMigrationTests(PtwApiFactory factory)
{
    private const string TaskMigration = "20260903081421_PersistPermitWorkflowTasks";
    private const string AttachmentEvidenceMigration = "20260915074426_AddV16AttachmentEvidenceMetadata";
    private const string UserAccountsMigration = "20260921092617_AddUserAccountsAndSignatures";
    private const string DemoModeMigration = "20260922030206_AddDemoModeSetting";

    [Fact]
    public async Task BusinessRevisionMigrationReconcilesLegacyVersionTwelveToRevisionOne()
    {
        var builder = new SqlConnectionStringBuilder(factory.ConnectionString)
        {
            InitialCatalog = $"PtwMigrationTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        await using var db = new PtwDbContext(options);

        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(DemoModeMigration);
            var now = DateTimeOffset.UtcNow;
            var permit = Permit(now, "Closed", "{}");
            permit.Version = 12;
            await InsertHistoricalPermitAsync(db, permit);
            var legacyRevisionId = Guid.CreateVersion7();
            const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            const string actor = "sponsor.migration";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [ptw].[PermitVersion]
                    ([Id], [PermitId], [Version], [ContentJson], [ContentHash], [CreatedAt], [CreatedBy])
                VALUES
                    ({legacyRevisionId}, {permit.Id}, 12, {permit.DraftJson}, {hash}, {now.AddMinutes(-2)}, {actor})
                """);
            var submitEventId = Guid.CreateVersion7();
            const string submitEventType = "permit_submitted";
            const string emptyPayload = "{}";
            const string correlationId = "migration-test";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [audit].[AuditEvent]
                    ([Id], [PermitId], [EventType], [ActorId], [OccurredAt], [PayloadJson], [CorrelationId])
                VALUES
                    ({submitEventId}, {permit.Id}, {submitEventType}, {actor},
                     {now.AddMinutes(-1)}, {emptyPayload}, {correlationId})
                """);
            var legacyTask = Task(permit, "AREA_CLOSE_VERIFICATION", "AreaOwnerManager", now);
            legacyTask.PermitVersion = 11;
            await InsertHistoricalTaskAsync(db, legacyTask);

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            var migrated = await db.Permits.SingleAsync(x => x.Id == permit.Id);
            Assert.Equal(12, migrated.Version);
            Assert.Equal(1, migrated.BusinessVersion);
            var revision = await db.PermitRevisions.SingleAsync(x => x.PermitId == permit.Id);
            Assert.Equal(1, revision.Version);
            Assert.Equal(hash, revision.ContentHash);
            var migratedTask = await db.PermitTasks.SingleAsync(x => x.Id == legacyTask.Id);
            Assert.Equal(11, migratedTask.PermitVersion);
            Assert.Equal(1, migratedTask.BusinessPermitVersion);
            var audit = await db.AuditEvents.SingleAsync(x =>
                x.PermitId == permit.Id && x.EventType == "permit_business_version_reconciled");
            Assert.True(await db.OutboxMessages.AnyAsync(x => x.Id == audit.Id));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task V16MigrationLeavesOneHseRouteAndOneAtomicAreaApprovalTask()
    {
        var builder = new SqlConnectionStringBuilder(factory.ConnectionString)
        {
            InitialCatalog = $"PtwMigrationTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        await using var db = new PtwDbContext(options);

        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(TaskMigration);

            var now = DateTimeOffset.UtcNow;
            var hsseCompleted = Permit(now, "UnderReview", HsseEvidence(now));
            var awaitingHsse = Permit(now, "UnderReview", "{}");
            var alreadyAwaitingApproval = Permit(now, "AwaitingApproval", HsseEvidence(now));
            await InsertHistoricalPermitAsync(db, hsseCompleted);
            await InsertHistoricalPermitAsync(db, awaitingHsse);
            await InsertHistoricalPermitAsync(db, alreadyAwaitingApproval);
            await InsertHistoricalTaskAsync(
                db,
                Task(hsseCompleted, "GAS_DISTRIBUTION_VALIDATION", "GasDistributionValidator", now));
            await InsertHistoricalTaskAsync(
                db,
                Task(awaitingHsse, "GAS_DISTRIBUTION_VALIDATION", "GasDistributionValidator", now));
            await InsertHistoricalTaskAsync(
                db,
                Task(awaitingHsse, "HSSE_VALIDATION", "HSSEValidator", now));

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            Assert.Equal(
                "AwaitingAreaApproval",
                (await db.Permits.SingleAsync(x => x.Id == hsseCompleted.Id)).Status);
            Assert.Equal(
                "UnderValidation",
                (await db.Permits.SingleAsync(x => x.Id == awaitingHsse.Id)).Status);
            Assert.Empty(await db.PermitTasks.Where(
                x => x.Type == "GAS_DISTRIBUTION_VALIDATION" && x.Status == "PENDING").ToListAsync());
            Assert.Equal(
                2,
                await db.PermitTasks.CountAsync(
                    x => x.Type == "GAS_DISTRIBUTION_VALIDATION" && x.Status == "CANCELLED"));
            Assert.Single(await db.PermitTasks.Where(
                x => x.PermitId == awaitingHsse.Id
                    && x.Type == "HSE_VALIDATION"
                    && x.Status == "PENDING").ToListAsync());
            Assert.Single(await db.PermitTasks.Where(
                x => x.PermitId == hsseCompleted.Id
                    && x.Type == "AREA_APPROVE_AND_ISSUE"
                    && x.Status == "PENDING").ToListAsync());
            Assert.Single(await db.PermitTasks.Where(
                x => x.PermitId == alreadyAwaitingApproval.Id
                    && x.Type == "AREA_APPROVE_AND_ISSUE"
                    && x.Status == "PENDING").ToListAsync());
            var audits = await db.AuditEvents
                .Where(x => x.EventType == "workflow_route_reconciled")
                .ToListAsync();
            var outbox = await db.OutboxMessages
                .Where(x => x.EventType == "workflow_route_reconciled")
                .ToListAsync();
            Assert.Equal(3, audits.Count);
            Assert.Equal(3, outbox.Count);
            Assert.All(audits, audit => Assert.Contains(outbox, message => message.Id == audit.Id));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task MalwareEvidenceMigrationDowngradesUnverifiableCleanAttachment()
    {
        var builder = new SqlConnectionStringBuilder(factory.ConnectionString)
        {
            InitialCatalog = $"PtwMigrationTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        await using var db = new PtwDbContext(options);

        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(AttachmentEvidenceMigration);
            var now = DateTimeOffset.UtcNow;
            var permit = Permit(now, "Draft", "{}");
            await InsertHistoricalPermitAsync(db, permit);
            var attachmentId = Guid.CreateVersion7();
            const string fileName = "legacy.pdf";
            const string mediaType = "application/pdf";
            var sha256 = new string('A', 64);
            var storageKey = $"{attachmentId:N}.pdf";
            const string cleanStatus = "CLEAN";
            const string category = "SUPPORTING";
            const string uploader = "sponsor.migration";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [ptw].[PermitAttachment]
                    ([Id], [PermitId], [AddedInVersion], [RemovedInVersion], [FileName], [SizeBytes],
                     [MediaType], [Sha256], [StorageKey], [ScanStatus], [Category], [DocumentNumber],
                     [DocumentRevision], [DocumentDate], [TargetPermitVersion], [PrintPackageId],
                     [SupersedesAttachmentId], [UploadedBy], [UploadedAt], [RemovedBy], [RemovedAt])
                VALUES
                    ({attachmentId}, {permit.Id}, 1, NULL, {fileName}, 8,
                     {mediaType}, {sha256}, {storageKey}, {cleanStatus},
                     {category}, NULL, NULL, NULL, 1, NULL, NULL, {uploader}, {now}, NULL, NULL)
                """);

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            var migrated = await db.PermitAttachments.SingleAsync(x => x.Id == attachmentId);
            Assert.Equal("PENDING", migrated.ScanStatus);
            Assert.Null(migrated.ScanEvidenceReference);
            Assert.Null(migrated.ScannedAt);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task SponsorRevisionTaskMigrationBackfillsExistingRevisionRequiredPermit()
    {
        var builder = new SqlConnectionStringBuilder(factory.ConnectionString)
        {
            InitialCatalog = $"PtwMigrationTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        await using var db = new PtwDbContext(options);

        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(UserAccountsMigration);
            var now = DateTimeOffset.UtcNow;
            var permit = Permit(now, "RevisionRequired", "{}");
            await InsertHistoricalPermitAsync(db, permit);

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            var task = await db.PermitTasks.SingleAsync(x =>
                x.PermitId == permit.Id && x.Type == "SPONSOR_REVISION");
            Assert.Equal(permit.Version, task.BusinessPermitVersion);
            Assert.Equal("Sponsor", task.RequiredRole);
            Assert.Equal(permit.SponsorId, task.AssignedActorId);
            Assert.Equal("PENDING", task.Status);
            Assert.Equal("Perbaiki dan ajukan ulang PTW", task.Label);
            Assert.True(await db.AuditEvents.AnyAsync(x =>
                x.PermitId == permit.Id && x.EventType == "sponsor_revision_task_reconciled"));
            Assert.True(await db.OutboxMessages.AnyAsync(x =>
                x.AggregateId == permit.Id && x.EventType == "sponsor_revision_task_reconciled"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static PermitRecord Permit(DateTimeOffset now, string status, string workflowJson) => new()
    {
        Id = Guid.CreateVersion7(),
        PermitNumber = $"PTW-MIG-{Guid.NewGuid():N}",
        Status = status,
        Version = 1,
        LocationId = "MIGRATION-AREA",
        SponsorId = "sponsor.migration",
        ValidFrom = now.AddHours(-1),
        ValidUntil = now.AddHours(8),
        DraftJson = "{}",
        CreatedAt = now.AddHours(-1),
        UpdatedAt = now,
        WorkflowEvidenceJson = workflowJson
    };

    private static PermitTaskRecord Task(
        PermitRecord permit,
        string type,
        string role,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            PermitId = permit.Id,
            PermitVersion = permit.Version,
            Type = type,
            Label = type,
            RequiredRole = role,
            Status = "PENDING",
            CreatedAt = now
        };

    private static Task<int> InsertHistoricalPermitAsync(PtwDbContext db, PermitRecord permit) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ptw].[Permit]
                ([Id], [PermitNumber], [Status], [Version], [LocationId], [SponsorId],
                 [ValidFrom], [ValidUntil], [DraftJson], [CreatedAt], [UpdatedAt],
                 [ActiveWorkPeriodId], [SuspensionReason], [WorkflowEvidenceJson])
            VALUES
                ({permit.Id}, {permit.PermitNumber}, {permit.Status}, {permit.Version},
                 {permit.LocationId}, {permit.SponsorId}, {permit.ValidFrom}, {permit.ValidUntil},
                 {permit.DraftJson}, {permit.CreatedAt}, {permit.UpdatedAt},
                 {permit.ActiveWorkPeriodId}, {permit.SuspensionReason}, {permit.WorkflowEvidenceJson})
            """);

    private static Task<int> InsertHistoricalTaskAsync(PtwDbContext db, PermitTaskRecord task) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [wf].[PermitTask]
                ([Id], [PermitId], [PermitVersion], [Type], [Label], [RequiredRole],
                 [AssignedActorId], [Status], [CreatedAt], [CompletedAt], [CompletedBy], [CancelledAt])
            VALUES
                ({task.Id}, {task.PermitId}, {task.PermitVersion}, {task.Type}, {task.Label},
                 {task.RequiredRole}, {task.AssignedActorId}, {task.Status}, {task.CreatedAt},
                 {task.CompletedAt}, {task.CompletedBy}, {task.CancelledAt})
            """);

    private static string HsseEvidence(DateTimeOffset now) => JsonSerializer.Serialize(new
    {
        hsseValidation = new
        {
            kind = 0,
            actorId = "hsse.migration",
            statement = "Sesuai.",
            completedAt = now
        }
    });
}
