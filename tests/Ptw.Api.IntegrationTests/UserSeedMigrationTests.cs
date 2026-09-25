using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ptw.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class UserSeedMigrationTests(PtwApiFactory factory)
{
    private const string PreviousMigration = "20260922075901_PreserveLegacyPermitVersionLineage";
    private const string SeedCorrelationId = "migration:20260925004624";
    private const string SeedActor = "system:migration";
    private const string SourceOrfLocationId = "01a0a755-d839-7764-b2e5-8d0b7b9bece0";
    private const string SuperAdminSubjectId = "superadmin.local";
    private const string HseValidatorRole = "HSEValidator";
    private static readonly Guid SeededAdministratorAssignmentId = Guid.Parse("01A0C444-3330-754D-9781-A2371A5F306F");
    private static readonly Guid SeededValidatorAssignmentId = Guid.Parse("01A0C485-DA7E-79BA-921B-2FC75E108B3F");
    private static readonly string[] SeededSubjects =
    [
        SuperAdminSubjectId,
        "yosep.zulkarnain",
        "ade.ruhimat",
        "muhamad.ibrahim",
        "benny.sulistio",
        "erwin.jonathan",
        "darsono4",
        "aldi.setiawan",
        "mk.ferry.febrian",
        "mk.revanza.raytama"
    ];
    private static readonly string[] AreaScopedSubjects =
    [
        "yosep.zulkarnain",
        "ade.ruhimat",
        "muhamad.ibrahim",
        "benny.sulistio"
    ];
    private static readonly int[] ExpectedVersions = [1, 2, 3];

    [Fact]
    public async Task SeedMigrationInsertsAccountsAndSkipsAreaScopedAssignmentsWithoutOrfLocation()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();

            await migrator.MigrateAsync();

            var accounts = await db.UserAccounts.ToListAsync();
            Assert.Equal(SeededSubjects.Order(), accounts.Select(x => x.SubjectId).Order());
            Assert.All(accounts, account =>
            {
                Assert.True(account.IsActive);
                Assert.Equal(account.UserName.ToUpperInvariant(), account.NormalizedUserName);
            });
            var admin = Assert.Single(accounts, x => x.SubjectId == SuperAdminSubjectId);
            Assert.Equal("superadmin", admin.UserName);
            Assert.Equal("Super Administrator", admin.DisplayName);
            Assert.Empty(await db.UserCredentials.ToListAsync());
            Assert.Empty(await db.UserSignatureVersions.ToListAsync());

            var assignments = await db.UserAuthorizations.ToListAsync();
            Assert.Equal(6, assignments.Count);
            Assert.All(assignments, assignment =>
            {
                Assert.Equal("Approved", assignment.Status);
                Assert.Equal(3, assignment.Version);
                Assert.Equal("Direct", assignment.Kind);
                Assert.Null(assignment.LocationId);
                Assert.DoesNotContain(assignment.SubjectId, AreaScopedSubjects);
            });
            Assert.Contains(assignments, x => x.Id == SeededAdministratorAssignmentId
                && x.SubjectId == SuperAdminSubjectId && x.RoleCode == "Administrator");
            Assert.Contains(assignments, x => x.SubjectId == "mk.revanza.raytama" && x.RoleCode == "Sponsor");
            Assert.Equal(4, assignments.Count(x => x.RoleCode == HseValidatorRole));

            var versions = await db.UserAuthorizationVersions.ToListAsync();
            Assert.Equal(18, versions.Count);
            Assert.All(versions, version => Assert.Equal(Sha256Hex(version.ContentJson), version.ContentHash));
            Assert.All(assignments, assignment => Assert.Equal(
                ExpectedVersions,
                versions.Where(x => x.UserAuthorizationId == assignment.Id).Select(x => x.Version).Order()));

            var userEvents = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "user_account_seeded")
                .ToListAsync();
            Assert.Equal(10, userEvents.Count);
            Assert.All(userEvents, audit =>
            {
                Assert.Equal("UserAccount", audit.AggregateType);
                Assert.Equal(SeedActor, audit.ActorId);
                Assert.Equal(SeedCorrelationId, audit.CorrelationId);
            });
            Assert.Contains(userEvents, x => x.AggregateId == DeterministicGuid(SuperAdminSubjectId));
            var seededEvents = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "authorization_seeded")
                .ToListAsync();
            Assert.Equal(6, seededEvents.Count);
            Assert.All(seededEvents, audit => Assert.Contains(assignments, x => x.Id == audit.AggregateId));
            var skippedEvents = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "authorization_seed_skipped")
                .ToListAsync();
            Assert.Equal(4, skippedEvents.Count);
            Assert.All(skippedEvents, audit =>
                Assert.Contains("\"reason\":\"location_not_found\"", audit.PayloadJson));

            var outbox = await db.OutboxMessages
                .Where(x => x.EventType == "user_account_seeded"
                    || x.EventType == "authorization_seeded"
                    || x.EventType == "authorization_seed_skipped")
                .ToListAsync();
            Assert.Equal(16, outbox.Count);
            Assert.DoesNotContain(outbox, x => x.EventType == "authorization_seed_skipped");
            Assert.All(userEvents.Concat(seededEvents), audit => Assert.Contains(outbox, x => x.Id == audit.Id));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task SeedMigrationResolvesAreaScopedAssignmentsToExistingOrfLocation()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var orfLocationId = Guid.CreateVersion7();
            await InsertLocationAsync(db, orfLocationId, "ORF", "Onshore Receiving Facility");

            await migrator.MigrateAsync();

            var assignments = await db.UserAuthorizations.ToListAsync();
            Assert.Equal(10, assignments.Count);
            var areaScoped = assignments.Where(x => AreaScopedSubjects.Contains(x.SubjectId)).ToList();
            Assert.Equal(4, areaScoped.Count);
            Assert.All(areaScoped, assignment => Assert.Equal(orfLocationId, assignment.LocationId));
            Assert.All(assignments.Except(areaScoped), assignment => Assert.Null(assignment.LocationId));

            var versions = await db.UserAuthorizationVersions.ToListAsync();
            Assert.Equal(30, versions.Count);
            var areaVersions = versions
                .Where(version => areaScoped.Any(x => x.Id == version.UserAuthorizationId))
                .ToList();
            Assert.Equal(12, areaVersions.Count);
            Assert.All(areaVersions, version =>
            {
                Assert.Contains($"\"locationId\":\"{orfLocationId:D}\"", version.ContentJson);
                Assert.DoesNotContain(SourceOrfLocationId, version.ContentJson);
                Assert.Equal(Sha256Hex(version.ContentJson), version.ContentHash);
            });

            Assert.Equal(10, await db.ConfigurationAuditEvents.CountAsync(x => x.EventType == "authorization_seeded"));
            Assert.Equal(0, await db.ConfigurationAuditEvents.CountAsync(x => x.EventType == "authorization_seed_skipped"));
            var seededArea = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "authorization_seeded" && x.PayloadJson.Contains("\"locationCode\":\"ORF\""))
                .ToListAsync();
            Assert.Equal(4, seededArea.Count);
            Assert.All(seededArea, audit =>
                Assert.Contains($"\"locationId\":\"{orfLocationId:D}\"", audit.PayloadJson));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task SeedMigrationKeepsExistingAccountAndSkipsDuplicateApprovedRole()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var now = DateTimeOffset.UtcNow;
            const string existingAdminName = "Existing Administrator";
            const string existingValidator = "darsono4";
            await InsertAccountAsync(db, SuperAdminSubjectId, "superadmin", existingAdminName, now);
            await InsertAccountAsync(db, existingValidator, existingValidator, "Darsono (existing)", now);
            var existingAssignmentId = Guid.CreateVersion7();
            await InsertApprovedAssignmentAsync(db, existingAssignmentId, existingValidator, HseValidatorRole, now);

            await migrator.MigrateAsync();

            var admin = await db.UserAccounts.SingleAsync(x => x.SubjectId == SuperAdminSubjectId);
            Assert.Equal(existingAdminName, admin.DisplayName);
            Assert.Equal(10, await db.UserAccounts.CountAsync());
            var userEvents = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "user_account_seeded")
                .ToListAsync();
            Assert.Equal(8, userEvents.Count);
            Assert.DoesNotContain(userEvents, x => x.AggregateId == DeterministicGuid(SuperAdminSubjectId));
            Assert.DoesNotContain(userEvents, x => x.AggregateId == DeterministicGuid(existingValidator));

            Assert.Contains(
                await db.UserAuthorizations.ToListAsync(),
                x => x.Id == SeededAdministratorAssignmentId && x.SubjectId == SuperAdminSubjectId);
            var validatorAssignment = Assert.Single(await db.UserAuthorizations
                .Where(x => x.SubjectId == existingValidator && x.RoleCode == HseValidatorRole)
                .ToListAsync());
            Assert.Equal(existingAssignmentId, validatorAssignment.Id);
            Assert.Empty(await db.UserAuthorizationVersions
                .Where(x => x.UserAuthorizationId == SeededValidatorAssignmentId)
                .ToListAsync());
            // No ORF location exists here either, so the four area-scoped rows are skipped as well.
            var skippedEvents = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "authorization_seed_skipped")
                .ToListAsync();
            Assert.Equal(5, skippedEvents.Count);
            var duplicate = Assert.Single(skippedEvents, x => x.PayloadJson.Contains("\"reason\":\"approved_role_exists\""));
            Assert.Contains($"\"subjectId\":\"{existingValidator}\"", duplicate.PayloadJson);
            Assert.Equal(4, skippedEvents.Count(x => x.PayloadJson.Contains("\"reason\":\"location_not_found\"")));
            Assert.Equal(5, await db.ConfigurationAuditEvents.CountAsync(x => x.EventType == "authorization_seeded"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private PtwDbContext CreateIsolatedContext()
    {
        var builder = new SqlConnectionStringBuilder(factory.ConnectionString)
        {
            InitialCatalog = $"PtwSeedMigrationTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        return new PtwDbContext(options);
    }

    private static async Task InsertLocationAsync(PtwDbContext db, Guid id, string code, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var effectiveFrom = now.AddDays(-1);
        const string status = "Approved";
        const string maker = "location.maker";
        const string checker = "location.checker";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [cfg].[LocationMaster]
                ([Id], [Code], [Name], [ParentId], [EffectiveFrom], [EffectiveUntil], [Status], [Version],
                 [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
            VALUES
                ({id}, {code}, {name}, NULL, {effectiveFrom}, NULL, {status}, 3,
                 {maker}, {checker}, {now}, {now}, {now})
            """);
    }

    private static async Task InsertAccountAsync(
        PtwDbContext db,
        string subjectId,
        string userName,
        string displayName,
        DateTimeOffset now)
    {
        var normalized = userName.ToUpperInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [sec].[UserAccount]
                ([SubjectId], [UserName], [NormalizedUserName], [DisplayName], [Position], [Department],
                 [IsActive], [Version], [CreatedAt], [UpdatedAt])
            VALUES
                ({subjectId}, {userName}, {normalized}, {displayName}, NULL, NULL, 1, 1, {now}, {now})
            """);
    }

    private static async Task InsertApprovedAssignmentAsync(
        PtwDbContext db,
        Guid id,
        string subjectId,
        string roleCode,
        DateTimeOffset now)
    {
        const string actions = "[\"permit.validate\"]";
        const string competencies = "[]";
        const string kind = "Direct";
        const string status = "Approved";
        const string maker = "admin.maker";
        const string checker = "admin.checker";
        var effectiveFrom = now.AddDays(-1);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [sec].[UserAuthorization]
                ([Id], [SubjectId], [RoleCode], [ActionCodesJson], [LocationId], [IncludeDescendants],
                 [RequiredCompetencyCodesJson], [Kind], [SourceAuthorizationId], [EffectiveFrom], [EffectiveUntil],
                 [Status], [Version], [MakerId], [CheckerId], [ApprovedAt], [CreatedAt], [UpdatedAt])
            VALUES
                ({id}, {subjectId}, {roleCode}, {actions}, NULL, 0,
                 {competencies}, {kind}, NULL, {effectiveFrom}, NULL,
                 {status}, 3, {maker}, {checker}, {now}, {now}, {now})
            """);
    }

    private static string Sha256Hex(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    // Mirrors UserDirectoryStore.DeterministicGuid so the seeded audit rows line up with the
    // aggregate id the application uses for the same account.
    private static Guid DeterministicGuid(string subjectId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subjectId.ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }
}
