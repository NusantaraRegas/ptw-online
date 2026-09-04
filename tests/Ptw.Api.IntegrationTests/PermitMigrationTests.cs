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

    [Fact]
    public async Task RetiredGasRouteReconcilesInFlightPermitsAndPendingTasks()
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
            db.PermitTasks.AddRange(
                Task(hsseCompleted, "GAS_DISTRIBUTION_VALIDATION", "GasDistributionValidator", now),
                Task(awaitingHsse, "GAS_DISTRIBUTION_VALIDATION", "GasDistributionValidator", now),
                Task(awaitingHsse, "HSSE_VALIDATION", "HSSEValidator", now));
            await db.SaveChangesAsync();

            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();

            Assert.Equal(
                "AwaitingApproval",
                (await db.Permits.SingleAsync(x => x.Id == hsseCompleted.Id)).Status);
            Assert.Equal(
                "UnderReview",
                (await db.Permits.SingleAsync(x => x.Id == awaitingHsse.Id)).Status);
            Assert.Empty(await db.PermitTasks.Where(
                x => x.Type == "GAS_DISTRIBUTION_VALIDATION" && x.Status == "PENDING").ToListAsync());
            Assert.Equal(
                2,
                await db.PermitTasks.CountAsync(
                    x => x.Type == "GAS_DISTRIBUTION_VALIDATION" && x.Status == "CANCELLED"));
            Assert.Single(await db.PermitTasks.Where(
                x => x.PermitId == awaitingHsse.Id
                    && x.Type == "HSSE_VALIDATION"
                    && x.Status == "PENDING").ToListAsync());
            Assert.Single(await db.PermitTasks.Where(
                x => x.PermitId == hsseCompleted.Id
                    && x.Type == "AREA_OWNER_APPROVAL"
                    && x.Status == "PENDING").ToListAsync());
            Assert.Single(await db.PermitTasks.Where(
                x => x.PermitId == alreadyAwaitingApproval.Id
                    && x.Type == "AREA_OWNER_APPROVAL"
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
