using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ptw.Infrastructure.Persistence;
using System.Security.Cryptography;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class DefaultSignatureSeedMigrationTests(PtwApiFactory factory)
{
    private const string BeforeDefaultSpecimenMigration = "20261020035834_ScopePermitTaskVersionIndexToPending";
    private const string SeedCorrelationId = "migration:20261021025910";
    private const string SeedActor = "system:migration";
    public const string PlaceholderSha256 = "A8B9EA3907F92F8ED6FB65783E993C58D2725F8B5384B3B6DFE22FB429BE540F";
    public const string DefaultSpecimenSha256 = "63549E64E3AF4C5D51BA2D977BB381A44206C67F15CAEB9B3F5C9044EA36B1D8";
    public const int DefaultSpecimenLength = 476;
    private static readonly string[] SeededSubjects =
    [
        "superadmin.local",
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

    [Fact]
    public async Task FreshDatabaseSupersedesEveryPlaceholderWithTheDefaultSpecimen()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();

            await migrator.MigrateAsync();

            var signatures = await db.UserSignatureVersions.ToListAsync();
            Assert.Equal(20, signatures.Count);
            foreach (var subjectId in SeededSubjects)
            {
                var versions = signatures.Where(x => x.SubjectId == subjectId).OrderBy(x => x.Version).ToList();
                Assert.Equal(2, versions.Count);
                var placeholder = versions[0];
                Assert.Equal(1, placeholder.Version);
                Assert.False(placeholder.IsActive);
                Assert.NotNull(placeholder.SupersededAt);
                Assert.Equal(PlaceholderSha256, placeholder.Sha256);
                var current = versions[1];
                Assert.Equal(2, current.Version);
                Assert.True(current.IsActive);
                Assert.Null(current.SupersededAt);
                Assert.Equal("image/png", current.MediaType);
                Assert.Equal(DefaultSpecimenLength, current.Content.Length);
                Assert.Equal(DefaultSpecimenSha256, current.Sha256);
                Assert.Equal(DefaultSpecimenSha256, Convert.ToHexString(SHA256.HashData(current.Content)));
                Assert.Equal(SeedActor, current.UploadedBy);
                Assert.StartsWith("\x89PNG", System.Text.Encoding.Latin1.GetString(current.Content.AsSpan(0, 4)));
            }

            var events = await db.ConfigurationAuditEvents
                .Where(x => x.CorrelationId == SeedCorrelationId)
                .ToListAsync();
            Assert.Equal(10, events.Count);
            Assert.All(events, audit =>
            {
                Assert.Equal("UserAccount", audit.AggregateType);
                Assert.Equal("user_signature_seeded", audit.EventType);
                Assert.Equal(SeedActor, audit.ActorId);
                Assert.Contains($"\"sha256\":\"{DefaultSpecimenSha256}\"", audit.PayloadJson);
                Assert.Contains("\"signatureVersion\":2", audit.PayloadJson);
                Assert.Contains("\"supersededSignatureVersion\":1", audit.PayloadJson);
                Assert.Contains("\"source\":\"default-specimen-2026-10-21\"", audit.PayloadJson);
            });
            foreach (var subjectId in SeededSubjects)
            {
                var audit = Assert.Single(events, x => x.AggregateId == DeterministicGuid(subjectId));
                var current = signatures.Single(x => x.SubjectId == subjectId && x.IsActive);
                var placeholder = signatures.Single(x => x.SubjectId == subjectId && !x.IsActive);
                Assert.Contains($"\"signatureId\":\"{current.Id:D}\"", audit.PayloadJson);
                Assert.Contains($"\"supersededSignatureId\":\"{placeholder.Id:D}\"", audit.PayloadJson);
            }
            var outbox = await db.OutboxMessages.Where(x => x.EventType == "user_signature_seeded").ToListAsync();
            Assert.All(events, audit => Assert.Contains(outbox, message => message.Id == audit.Id));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task OperatorUploadedSpecimenIsLeftUntouched()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            var migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeDefaultSpecimenMigration);
            const string operatorSubject = "darsono4";
            var operatorSpecimenId = Guid.CreateVersion7();
            var operatorSpecimen = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03 };
            var operatorSha256 = Convert.ToHexString(SHA256.HashData(operatorSpecimen));
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE [sec].[UserSignatureVersion]
                SET [IsActive] = 0, [SupersededAt] = {now}
                WHERE [SubjectId] = {operatorSubject}
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [sec].[UserSignatureVersion]
                    ([Id], [SubjectId], [Version], [MediaType], [Content], [Sha256], [UploadedBy], [UploadedAt],
                     [IsActive], [SupersededAt])
                VALUES
                    ({operatorSpecimenId}, {operatorSubject}, 2, 'image/png', {operatorSpecimen}, {operatorSha256},
                     'superadmin.local', {now}, 1, NULL)
                """);

            await migrator.MigrateAsync();

            var signatures = await db.UserSignatureVersions.ToListAsync();
            Assert.Equal(20, signatures.Count);
            var operatorVersions = signatures.Where(x => x.SubjectId == operatorSubject).ToList();
            Assert.Equal(2, operatorVersions.Count);
            var operatorActive = Assert.Single(operatorVersions, x => x.IsActive);
            Assert.Equal(operatorSpecimenId, operatorActive.Id);
            Assert.Equal(operatorSha256, operatorActive.Sha256);
            Assert.DoesNotContain(operatorVersions, x => x.Sha256 == DefaultSpecimenSha256);

            var defaults = signatures.Where(x => x.Sha256 == DefaultSpecimenSha256).ToList();
            Assert.Equal(9, defaults.Count);
            Assert.All(defaults, signature => Assert.True(signature.IsActive));
            Assert.DoesNotContain(defaults, x => x.SubjectId == operatorSubject);

            var events = await db.ConfigurationAuditEvents
                .Where(x => x.CorrelationId == SeedCorrelationId)
                .ToListAsync();
            Assert.Equal(9, events.Count);
            Assert.DoesNotContain(events, x => x.AggregateId == DeterministicGuid(operatorSubject));
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
            InitialCatalog = $"PtwDefaultSpecimenSeedTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        return new PtwDbContext(options);
    }

    // Mirrors UserDirectoryStore.DeterministicGuid so seeded audit rows line up with the aggregate
    // id the application uses for the same account.
    private static Guid DeterministicGuid(string subjectId)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(subjectId.ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }
}
