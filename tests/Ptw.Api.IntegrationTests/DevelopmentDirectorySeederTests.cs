using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class DevelopmentDirectorySeederTests(PtwApiFactory factory)
{
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
    public async Task SeedsTheSharedPasswordForEverySeededAccountOnceAndLoginWorks()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            await db.Database.MigrateAsync();
            var clock = new TestClock();
            var seeder = new DevelopmentDirectorySeeder(db, clock, new DevelopmentSeedSettings(true));

            var result = await seeder.SeedLocalCredentialsAsync(CancellationToken.None);

            Assert.True(result.Enabled);
            Assert.Equal(SeededSubjects.Order(), result.SeededSubjectIds);
            Assert.Equal(0, result.SkippedWithCredential);
            var credentials = await db.UserCredentials.AsNoTracking().ToListAsync();
            Assert.Equal(SeededSubjects.Order(), credentials.Select(x => x.SubjectId).Order());
            Assert.All(credentials, credential =>
            {
                Assert.Equal(210_000, credential.Iterations);
                Assert.Equal(16, credential.PasswordSalt.Length);
                Assert.Equal(32, credential.PasswordHash.Length);
                Assert.Equal(0, credential.FailedAttempts);
                Assert.Null(credential.LockedUntil);
                Assert.Equal(clock.UtcNow, credential.PasswordChangedAt);
            });
            // Every account gets its own salt; the shared password never produces a shared hash.
            Assert.Equal(credentials.Count, credentials.Select(x => Convert.ToHexString(x.PasswordSalt)).Distinct().Count());

            var events = await db.ConfigurationAuditEvents.AsNoTracking()
                .Where(x => x.EventType == DevelopmentDirectorySeeder.EventType)
                .ToListAsync();
            Assert.Equal(SeededSubjects.Length, events.Count);
            Assert.All(events, audit =>
            {
                Assert.Equal("UserAccount", audit.AggregateType);
                Assert.Equal(DevelopmentDirectorySeeder.Actor, audit.ActorId);
                Assert.Equal(DevelopmentDirectorySeeder.CorrelationId, audit.CorrelationId);
                Assert.DoesNotContain(DevelopmentDirectorySeeder.SeededLocalPassword, audit.PayloadJson);
            });
            var outbox = await db.OutboxMessages.AsNoTracking()
                .Where(x => x.EventType == DevelopmentDirectorySeeder.EventType)
                .ToListAsync();
            Assert.Equal(SeededSubjects.Length, outbox.Count);
            Assert.All(outbox, message => Assert.DoesNotContain(DevelopmentDirectorySeeder.SeededLocalPassword, message.PayloadJson));

            var store = new UserDirectoryStore(db, clock);
            foreach (var userName in new[] { "superadmin", "yosep.zulkarnain", "darsono4" })
            {
                var login = await store.AuthenticateAsync(userName, DevelopmentDirectorySeeder.SeededLocalPassword, CancellationToken.None);
                Assert.Equal(LocalAuthenticationOutcome.Succeeded, login.Outcome);
                Assert.NotNull(login.Account?.Signature);
                Assert.Equal(DefaultSignatureSeedMigrationTests.DefaultSpecimenSha256, login.Account.Signature.Sha256);
            }
            var wrong = await store.AuthenticateAsync("superadmin", "Wrong-Password-123", CancellationToken.None);
            Assert.Equal(LocalAuthenticationOutcome.InvalidPassword, wrong.Outcome);

            // A second run is a no-op: no new credential, audit, or outbox row.
            var again = await seeder.SeedLocalCredentialsAsync(CancellationToken.None);
            Assert.Empty(again.SeededSubjectIds);
            Assert.Equal(SeededSubjects.Length, again.SkippedWithCredential);
            Assert.Equal(SeededSubjects.Length, await db.UserCredentials.CountAsync());
            Assert.Equal(SeededSubjects.Length, await db.ConfigurationAuditEvents.CountAsync(x => x.EventType == DevelopmentDirectorySeeder.EventType));
            Assert.Equal(SeededSubjects.Length, await db.OutboxMessages.CountAsync(x => x.EventType == DevelopmentDirectorySeeder.EventType));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ExistingCredentialAndOperatorAccountsAreLeftUntouched()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            await db.Database.MigrateAsync();
            var clock = new TestClock();
            var store = new UserDirectoryStore(db, clock);
            const string ownPasswordSubject = "yosep.zulkarnain";
            const string ownPassword = "Operator-Chosen-Password-123";
            var account = await store.FindAsync(ownPasswordSubject, CancellationToken.None);
            Assert.NotNull(account);
            await store.ResetPasswordAsync(
                ownPasswordSubject,
                ownPassword,
                account.ETag,
                new Actor(
                    "superadmin.local",
                    "Super Administrator",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Administrator" },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
                "test",
                CancellationToken.None);
            var ownHash = await db.UserCredentials.AsNoTracking()
                .Where(x => x.SubjectId == ownPasswordSubject)
                .Select(x => x.PasswordHash)
                .SingleAsync();
            const string operatorSubject = "operator.registered";
            await InsertAccountAsync(db, operatorSubject, "operator.registered", "Operator Registered", clock.UtcNow);
            var seeder = new DevelopmentDirectorySeeder(db, clock, new DevelopmentSeedSettings(true));

            var result = await seeder.SeedLocalCredentialsAsync(CancellationToken.None);

            Assert.Equal(SeededSubjects.Length - 1, result.SeededSubjectIds.Count);
            Assert.DoesNotContain(ownPasswordSubject, result.SeededSubjectIds);
            Assert.DoesNotContain(operatorSubject, result.SeededSubjectIds);
            Assert.Equal(1, result.SkippedWithCredential);
            var credentials = await db.UserCredentials.AsNoTracking().ToListAsync();
            Assert.Equal(SeededSubjects.Length, credentials.Count);
            Assert.DoesNotContain(credentials, x => x.SubjectId == operatorSubject);
            Assert.Equal(ownHash, credentials.Single(x => x.SubjectId == ownPasswordSubject).PasswordHash);
            var ownLogin = await store.AuthenticateAsync(ownPasswordSubject, ownPassword, CancellationToken.None);
            Assert.Equal(LocalAuthenticationOutcome.Succeeded, ownLogin.Outcome);
            var sharedLogin = await store.AuthenticateAsync(ownPasswordSubject, DevelopmentDirectorySeeder.SeededLocalPassword, CancellationToken.None);
            Assert.Equal(LocalAuthenticationOutcome.InvalidPassword, sharedLogin.Outcome);
            // An account without a credential row is refused like a wrong password.
            var operatorLogin = await store.AuthenticateAsync(operatorSubject, DevelopmentDirectorySeeder.SeededLocalPassword, CancellationToken.None);
            Assert.Equal(LocalAuthenticationOutcome.InvalidPassword, operatorLogin.Outcome);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task RefusesToSeedOutsideDevelopment()
    {
        await using var db = CreateIsolatedContext();
        try
        {
            await db.Database.MigrateAsync();
            var seeder = new DevelopmentDirectorySeeder(db, new TestClock(), new DevelopmentSeedSettings(false));

            var result = await seeder.SeedLocalCredentialsAsync(CancellationToken.None);

            Assert.False(result.Enabled);
            Assert.Empty(result.SeededSubjectIds);
            Assert.Empty(await db.UserCredentials.ToListAsync());
            Assert.Equal(0, await db.ConfigurationAuditEvents.CountAsync(x => x.EventType == DevelopmentDirectorySeeder.EventType));
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
            InitialCatalog = $"PtwDevelopmentSeedTest{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<PtwDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;
        return new PtwDbContext(options);
    }

    private static async Task InsertAccountAsync(
        PtwDbContext db,
        string subjectId,
        string userName,
        string displayName,
        DateTimeOffset now)
    {
        var normalized = userName.ToUpperInvariant();
        var stamp = Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [sec].[UserAccount]
                ([SubjectId], [UserName], [NormalizedUserName], [DisplayName], [Position], [Department],
                 [IsActive], [Version], [CreatedAt], [UpdatedAt], [SecurityStamp])
            VALUES
                ({subjectId}, {userName}, {normalized}, {displayName}, NULL, NULL, 1, 1, {now}, {now}, {stamp})
            """);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    }
}
