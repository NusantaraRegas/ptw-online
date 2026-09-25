using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ptw.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class ReferenceDataSeedMigrationTests(PtwApiFactory factory)
{
    private const string BeforeUserSeedMigration = "20260922075901_PreserveLegacyPermitVersionLineage";
    private const string BeforeReferenceSeedMigration = "20260925011304_HardenSessionsAndLoginAudit";
    private const string SeedCorrelationId = "migration:20260925040421";
    private const string SeedActor = "system:migration";
    private const string SourceOrfLocationId = "01a0a755-d839-7764-b2e5-8d0b7b9bece0";
    private const string PlaceholderSha256 = "A8B9EA3907F92F8ED6FB65783E993C58D2725F8B5384B3B6DFE22FB429BE540F";
    private const int PlaceholderLength = 2180;
    private static readonly Guid SourceOrfLocationGuid = Guid.Parse(SourceOrfLocationId);
    private static readonly string[] SeededLocationCodes = ["ORF", "SITE_OFFICE", "WATER_BASED"];
    private static readonly string[] AreaScopedSubjects =
    [
        "yosep.zulkarnain",
        "ade.ruhimat",
        "muhamad.ibrahim",
        "benny.sulistio"
    ];
    private static readonly int[] ExpectedVersions = [1, 2, 3];

    [Fact]
    public async Task FreshDatabaseReceivesLocationsSignaturesAndOrfAssignments()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();

            await migrator.MigrateAsync();

            var locations = await db.LocationMasters.ToListAsync();
            Assert.Equal(SeededLocationCodes.Order(), locations.Select(x => x.Code).Order());
            Assert.All(locations, location =>
            {
                Assert.Equal("Approved", location.Status);
                Assert.Equal(3, location.Version);
                Assert.Null(location.ParentId);
                Assert.Null(location.EffectiveUntil);
            });
            var orf = Assert.Single(locations, x => x.Code == "ORF");
            Assert.Equal(SourceOrfLocationGuid, orf.Id);
            var locationVersions = await db.LocationMasterVersions.ToListAsync();
            Assert.Equal(9, locationVersions.Count);
            Assert.All(locationVersions, version => Assert.Equal(Sha256Hex(version.ContentJson), version.ContentHash));
            Assert.All(locations, location => Assert.Equal(
                ExpectedVersions,
                locationVersions.Where(x => x.LocationMasterId == location.Id).Select(x => x.Version).Order()));

            var accounts = await db.UserAccounts.ToListAsync();
            Assert.Equal(10, accounts.Count);
            var signatures = await db.UserSignatureVersions.ToListAsync();
            Assert.Equal(10, signatures.Count);
            Assert.Equal(accounts.Select(x => x.SubjectId).Order(), signatures.Select(x => x.SubjectId).Order());
            Assert.All(signatures, signature =>
            {
                Assert.Equal(1, signature.Version);
                Assert.True(signature.IsActive);
                Assert.Null(signature.SupersededAt);
                Assert.Equal("image/png", signature.MediaType);
                Assert.Equal(PlaceholderLength, signature.Content.Length);
                Assert.Equal(PlaceholderSha256, signature.Sha256);
                Assert.Equal(PlaceholderSha256, Convert.ToHexString(SHA256.HashData(signature.Content)));
            });
            Assert.Empty(await db.UserCredentials.ToListAsync());

            var assignments = await db.UserAuthorizations.ToListAsync();
            Assert.Equal(10, assignments.Count);
            var areaScoped = assignments.Where(x => AreaScopedSubjects.Contains(x.SubjectId)).ToList();
            Assert.Equal(4, areaScoped.Count);
            Assert.All(areaScoped, assignment =>
            {
                Assert.Equal(SourceOrfLocationGuid, assignment.LocationId);
                Assert.Equal("Approved", assignment.Status);
                Assert.Equal(3, assignment.Version);
            });
            var versions = await db.UserAuthorizationVersions.ToListAsync();
            Assert.Equal(30, versions.Count);
            var areaVersions = versions.Where(v => areaScoped.Any(x => x.Id == v.UserAuthorizationId)).ToList();
            Assert.Equal(12, areaVersions.Count);
            Assert.All(areaVersions, version =>
            {
                Assert.Contains($"\"locationId\":\"{SourceOrfLocationId}\"", version.ContentJson);
                Assert.Equal(Sha256Hex(version.ContentJson), version.ContentHash);
            });

            var seedEvents = await db.ConfigurationAuditEvents
                .Where(x => x.CorrelationId == SeedCorrelationId)
                .ToListAsync();
            Assert.All(seedEvents, audit => Assert.Equal(SeedActor, audit.ActorId));
            Assert.Equal(3, seedEvents.Count(x => x.EventType == "location_seeded" && x.AggregateType == "LocationMaster"));
            Assert.Equal(10, seedEvents.Count(x => x.EventType == "user_signature_seeded" && x.AggregateType == "UserAccount"));
            Assert.Equal(4, seedEvents.Count(x => x.EventType == "authorization_seeded"));
            Assert.Equal(17, seedEvents.Count);
            Assert.All(areaScoped, assignment =>
                Assert.Contains(seedEvents, x => x.EventType == "authorization_seeded" && x.AggregateId == assignment.Id));
            Assert.Contains(seedEvents, x => x.AggregateId == DeterministicGuid("superadmin.local")
                && x.PayloadJson.Contains($"\"sha256\":\"{PlaceholderSha256}\""));
            var outbox = await db.OutboxMessages.ToListAsync();
            Assert.All(seedEvents, audit => Assert.Contains(outbox, message => message.Id == audit.Id));

            // The earlier user seed ran before any location existed and recorded the skip; this
            // migration then completed those four assignments.
            var earlierSkips = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "authorization_seed_skipped")
                .ToListAsync();
            Assert.Equal(4, earlierSkips.Count);
            Assert.All(earlierSkips, audit => Assert.NotEqual(SeedCorrelationId, audit.CorrelationId));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ExistingOrfLocationAndOperatorAccountAreRespected()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            var now = DateTimeOffset.UtcNow;
            const string operatorSubject = "darsono4";
            await migrator.MigrateAsync(BeforeUserSeedMigration);
            await InsertAccountAsync(db, operatorSubject, operatorSubject, "Darsono (existing)", now);
            await migrator.MigrateAsync(BeforeReferenceSeedMigration);
            var existingOrfId = Guid.CreateVersion7();
            await InsertLocationAsync(db, existingOrfId, "ORF", "ORF (existing)", now);

            await migrator.MigrateAsync();

            var locations = await db.LocationMasters.ToListAsync();
            Assert.Equal(SeededLocationCodes.Order(), locations.Select(x => x.Code).Order());
            var orf = Assert.Single(locations, x => x.Code == "ORF");
            Assert.Equal(existingOrfId, orf.Id);
            Assert.Equal("ORF (existing)", orf.Name);
            Assert.DoesNotContain(locations, x => x.Id == SourceOrfLocationGuid);
            Assert.Empty(await db.LocationMasterVersions
                .Where(x => x.LocationMasterId == SourceOrfLocationGuid)
                .ToListAsync());
            Assert.Equal(6, await db.LocationMasterVersions.CountAsync());
            var locationEvents = await db.ConfigurationAuditEvents
                .Where(x => x.EventType == "location_seeded")
                .ToListAsync();
            Assert.Equal(2, locationEvents.Count);
            Assert.DoesNotContain(locationEvents, x => x.AggregateId == existingOrfId);

            var areaScoped = await db.UserAuthorizations
                .Where(x => AreaScopedSubjects.Contains(x.SubjectId))
                .ToListAsync();
            Assert.Equal(4, areaScoped.Count);
            Assert.All(areaScoped, assignment => Assert.Equal(existingOrfId, assignment.LocationId));
            var areaVersions = await db.UserAuthorizationVersions.ToListAsync();
            areaVersions = areaVersions.Where(v => areaScoped.Any(x => x.Id == v.UserAuthorizationId)).ToList();
            Assert.Equal(12, areaVersions.Count);
            Assert.All(areaVersions, version =>
            {
                Assert.Contains($"\"locationId\":\"{existingOrfId:D}\"", version.ContentJson);
                Assert.DoesNotContain(SourceOrfLocationId, version.ContentJson);
                Assert.Equal(Sha256Hex(version.ContentJson), version.ContentHash);
            });

            var signatures = await db.UserSignatureVersions.ToListAsync();
            Assert.Equal(9, signatures.Count);
            Assert.DoesNotContain(signatures, x => x.SubjectId == operatorSubject);
            Assert.Equal(9, await db.ConfigurationAuditEvents.CountAsync(x => x.EventType == "user_signature_seeded"));
            Assert.Equal(0, await db.ConfigurationAuditEvents.CountAsync(
                x => x.CorrelationId == SeedCorrelationId && x.EventType == "authorization_seed_skipped"));
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
            InitialCatalog = $"PtwReferenceSeedTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        return new PtwDbContext(options);
    }

    private static async Task InsertLocationAsync(PtwDbContext db, Guid id, string code, string name, DateTimeOffset now)
    {
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

    private static string Sha256Hex(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    // Mirrors UserDirectoryStore.DeterministicGuid so seeded audit rows line up with the aggregate
    // id the application uses for the same account.
    private static Guid DeterministicGuid(string subjectId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subjectId.ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }
}
