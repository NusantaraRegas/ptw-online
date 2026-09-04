using Microsoft.EntityFrameworkCore;

namespace Ptw.Infrastructure.Persistence;

public sealed class PtwDbContext(DbContextOptions<PtwDbContext> options) : DbContext(options)
{
    public DbSet<PermitRecord> Permits => Set<PermitRecord>();
    public DbSet<PermitVersionRecord> PermitVersions => Set<PermitVersionRecord>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<PermitTaskRecord> PermitTasks => Set<PermitTaskRecord>();
    public DbSet<LocationMasterRecord> LocationMasters => Set<LocationMasterRecord>();
    public DbSet<LocationMasterVersionRecord> LocationMasterVersions => Set<LocationMasterVersionRecord>();
    public DbSet<ConfigurationAuditEventRecord> ConfigurationAuditEvents => Set<ConfigurationAuditEventRecord>();
    public DbSet<LocationCommandReceiptRecord> LocationCommandReceipts => Set<LocationCommandReceiptRecord>();
    public DbSet<UserAuthorizationRecord> UserAuthorizations => Set<UserAuthorizationRecord>();
    public DbSet<UserAuthorizationVersionRecord> UserAuthorizationVersions => Set<UserAuthorizationVersionRecord>();
    public DbSet<AuthorizationCommandReceiptRecord> AuthorizationCommandReceipts => Set<AuthorizationCommandReceiptRecord>();
    public DbSet<PolicyUatSuiteRecord> PolicyUatSuites => Set<PolicyUatSuiteRecord>();
    public DbSet<PolicyUatRunRecord> PolicyUatRuns => Set<PolicyUatRunRecord>();
    public DbSet<PolicyUatCommandReceiptRecord> PolicyUatCommandReceipts => Set<PolicyUatCommandReceiptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ptw");

        var permit = modelBuilder.Entity<PermitRecord>();
        permit.ToTable("Permit", "ptw");
        permit.HasKey(x => x.Id);
        permit.Property(x => x.PermitNumber).HasMaxLength(40);
        permit.HasIndex(x => x.PermitNumber).IsUnique().HasFilter("[PermitNumber] IS NOT NULL");
        permit.Property(x => x.Status).HasMaxLength(40);
        permit.Property(x => x.LocationId).HasMaxLength(100);
        permit.Property(x => x.SponsorId).HasMaxLength(200);
        permit.Property(x => x.RowVersion).IsRowVersion();
        permit.HasIndex(x => new { x.Status, x.LocationId });
        permit.HasIndex(x => new { x.SponsorId, x.UpdatedAt });

        var version = modelBuilder.Entity<PermitVersionRecord>();
        version.ToTable("PermitVersion", "ptw");
        version.HasKey(x => x.Id);
        version.HasIndex(x => new { x.PermitId, x.Version }).IsUnique();
        version.Property(x => x.ContentHash).HasMaxLength(64);
        version.Property(x => x.CreatedBy).HasMaxLength(200);

        var audit = modelBuilder.Entity<AuditEventRecord>();
        audit.ToTable("AuditEvent", "audit");
        audit.HasKey(x => x.Sequence);
        audit.Property(x => x.Sequence).UseIdentityColumn();
        audit.HasIndex(x => new { x.PermitId, x.OccurredAt });
        audit.Property(x => x.EventType).HasMaxLength(100);
        audit.Property(x => x.ActorId).HasMaxLength(200);
        audit.Property(x => x.CorrelationId).HasMaxLength(100);

        var outbox = modelBuilder.Entity<OutboxMessageRecord>();
        outbox.ToTable("OutboxMessage", "intg");
        outbox.HasKey(x => x.Id);
        outbox.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
        outbox.Property(x => x.EventType).HasMaxLength(100);

        var idempotency = modelBuilder.Entity<IdempotencyRecord>();
        idempotency.ToTable("Idempotency", "intg");
        idempotency.HasKey(x => x.Id);
        idempotency.HasIndex(x => new { x.ActorId, x.Operation, x.Key }).IsUnique();
        idempotency.Property(x => x.ActorId).HasMaxLength(200);
        idempotency.Property(x => x.Operation).HasMaxLength(100);
        idempotency.Property(x => x.Key).HasMaxLength(200);
        idempotency.Property(x => x.RequestHash).HasMaxLength(64);

        var permitTask = modelBuilder.Entity<PermitTaskRecord>();
        permitTask.ToTable(
            "PermitTask",
            "wf",
            table => table.HasCheckConstraint(
                "CK_PermitTask_Status",
                "[Status] IN ('PENDING', 'COMPLETED', 'CANCELLED')"));
        permitTask.HasKey(x => x.Id);
        permitTask.Property(x => x.Type).HasMaxLength(80);
        permitTask.Property(x => x.Label).HasMaxLength(200);
        permitTask.Property(x => x.RequiredRole).HasMaxLength(100);
        permitTask.Property(x => x.AssignedActorId).HasMaxLength(200);
        permitTask.Property(x => x.Status).HasMaxLength(20);
        permitTask.Property(x => x.CompletedBy).HasMaxLength(200);
        permitTask.HasIndex(x => new { x.PermitId, x.PermitVersion, x.Type }).IsUnique();
        permitTask.HasIndex(x => new { x.Status, x.RequiredRole, x.AssignedActorId, x.CreatedAt });
        permitTask.HasOne<PermitRecord>()
            .WithMany()
            .HasForeignKey(x => x.PermitId)
            .OnDelete(DeleteBehavior.Restrict);

        var location = modelBuilder.Entity<LocationMasterRecord>();
        location.ToTable(
            "LocationMaster",
            "cfg",
            table => table.HasCheckConstraint(
                "CK_LocationMaster_EffectivePeriod",
                "[EffectiveUntil] IS NULL OR [EffectiveUntil] > [EffectiveFrom]"));
        location.HasKey(x => x.Id);
        location.Property(x => x.Code).HasMaxLength(100);
        location.Property(x => x.Name).HasMaxLength(200);
        location.Property(x => x.Status).HasMaxLength(40);
        location.Property(x => x.MakerId).HasMaxLength(200);
        location.Property(x => x.CheckerId).HasMaxLength(200);
        location.Property(x => x.RowVersion).IsRowVersion();
        location.HasIndex(x => new { x.Code, x.EffectiveFrom }).IsUnique();
        location.HasIndex(x => new { x.Status, x.EffectiveFrom, x.EffectiveUntil });
        location.HasOne<LocationMasterRecord>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        var locationVersion = modelBuilder.Entity<LocationMasterVersionRecord>();
        locationVersion.ToTable("LocationMasterVersion", "cfg");
        locationVersion.HasKey(x => x.Id);
        locationVersion.HasIndex(x => new { x.LocationMasterId, x.Version }).IsUnique();
        locationVersion.Property(x => x.ContentHash).HasMaxLength(64);
        locationVersion.Property(x => x.CreatedBy).HasMaxLength(200);
        locationVersion.HasOne<LocationMasterRecord>()
            .WithMany()
            .HasForeignKey(x => x.LocationMasterId)
            .OnDelete(DeleteBehavior.Restrict);

        var configurationAudit = modelBuilder.Entity<ConfigurationAuditEventRecord>();
        configurationAudit.ToTable("ConfigurationAuditEvent", "audit");
        configurationAudit.HasKey(x => x.Sequence);
        configurationAudit.Property(x => x.Sequence).UseIdentityColumn();
        configurationAudit.Property(x => x.AggregateType).HasMaxLength(100);
        configurationAudit.Property(x => x.EventType).HasMaxLength(100);
        configurationAudit.Property(x => x.ActorId).HasMaxLength(200);
        configurationAudit.Property(x => x.CorrelationId).HasMaxLength(100);
        configurationAudit.HasIndex(x => new { x.AggregateType, x.AggregateId, x.Sequence });

        var locationReceipt = modelBuilder.Entity<LocationCommandReceiptRecord>();
        locationReceipt.ToTable("LocationCommandReceipt", "intg");
        locationReceipt.HasKey(x => x.Id);
        locationReceipt.HasIndex(x => new { x.ActorId, x.Operation, x.Key }).IsUnique();
        locationReceipt.Property(x => x.ActorId).HasMaxLength(200);
        locationReceipt.Property(x => x.Operation).HasMaxLength(100);
        locationReceipt.Property(x => x.Key).HasMaxLength(200);
        locationReceipt.Property(x => x.RequestHash).HasMaxLength(64);
        locationReceipt.HasOne<LocationMasterRecord>()
            .WithMany()
            .HasForeignKey(x => x.LocationMasterId)
            .OnDelete(DeleteBehavior.Restrict);

        var userAuthorization = modelBuilder.Entity<UserAuthorizationRecord>();
        userAuthorization.ToTable(
            "UserAuthorization",
            "sec",
            table => table.HasCheckConstraint(
                "CK_UserAuthorization_EffectivePeriod",
                "[EffectiveUntil] IS NULL OR [EffectiveUntil] > [EffectiveFrom]"));
        userAuthorization.HasKey(x => x.Id);
        userAuthorization.Property(x => x.SubjectId).HasMaxLength(200);
        userAuthorization.Property(x => x.RoleCode).HasMaxLength(100);
        userAuthorization.Property(x => x.Kind).HasMaxLength(40);
        userAuthorization.Property(x => x.Status).HasMaxLength(40);
        userAuthorization.Property(x => x.MakerId).HasMaxLength(200);
        userAuthorization.Property(x => x.CheckerId).HasMaxLength(200);
        userAuthorization.Property(x => x.RowVersion).IsRowVersion();
        userAuthorization.HasIndex(x => new { x.SubjectId, x.Status, x.EffectiveFrom, x.EffectiveUntil });
        userAuthorization.HasIndex(x => new { x.LocationId, x.RoleCode, x.Status });
        userAuthorization.HasOne<LocationMasterRecord>()
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        userAuthorization.HasOne<UserAuthorizationRecord>()
            .WithMany()
            .HasForeignKey(x => x.SourceAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        var authorizationVersion = modelBuilder.Entity<UserAuthorizationVersionRecord>();
        authorizationVersion.ToTable("UserAuthorizationVersion", "sec");
        authorizationVersion.HasKey(x => x.Id);
        authorizationVersion.HasIndex(x => new { x.UserAuthorizationId, x.Version }).IsUnique();
        authorizationVersion.Property(x => x.ContentHash).HasMaxLength(64);
        authorizationVersion.Property(x => x.CreatedBy).HasMaxLength(200);
        authorizationVersion.HasOne<UserAuthorizationRecord>()
            .WithMany()
            .HasForeignKey(x => x.UserAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        var authorizationReceipt = modelBuilder.Entity<AuthorizationCommandReceiptRecord>();
        authorizationReceipt.ToTable("AuthorizationCommandReceipt", "intg");
        authorizationReceipt.HasKey(x => x.Id);
        authorizationReceipt.HasIndex(x => new { x.ActorId, x.Operation, x.Key }).IsUnique();
        authorizationReceipt.Property(x => x.ActorId).HasMaxLength(200);
        authorizationReceipt.Property(x => x.Operation).HasMaxLength(100);
        authorizationReceipt.Property(x => x.Key).HasMaxLength(200);
        authorizationReceipt.Property(x => x.RequestHash).HasMaxLength(64);
        authorizationReceipt.HasOne<UserAuthorizationRecord>()
            .WithMany()
            .HasForeignKey(x => x.UserAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        var policyUatSuite = modelBuilder.Entity<PolicyUatSuiteRecord>();
        policyUatSuite.ToTable("PolicyUatSuite", "cfg");
        policyUatSuite.HasKey(x => x.Id);
        policyUatSuite.Property(x => x.SuiteKey).HasMaxLength(100);
        policyUatSuite.Property(x => x.Name).HasMaxLength(200);
        policyUatSuite.Property(x => x.PolicyVersion).HasMaxLength(100);
        policyUatSuite.Property(x => x.ContentHash).HasMaxLength(64);
        policyUatSuite.Property(x => x.CreatedBy).HasMaxLength(200);
        policyUatSuite.HasIndex(x => new { x.SuiteKey, x.Version }).IsUnique();
        policyUatSuite.HasIndex(x => new { x.PolicyVersion, x.CreatedAt });

        var policyUatRun = modelBuilder.Entity<PolicyUatRunRecord>();
        policyUatRun.ToTable(
            "PolicyUatRun",
            "audit",
            table => table.HasCheckConstraint(
                "CK_PolicyUatRun_Counts",
                "[ScenarioCount] > 0 AND [MatchedCount] >= 0 AND [MatchedCount] <= [ScenarioCount]"));
        policyUatRun.HasKey(x => x.Id);
        policyUatRun.Property(x => x.PolicyVersion).HasMaxLength(100);
        policyUatRun.Property(x => x.SuiteContentHash).HasMaxLength(64);
        policyUatRun.Property(x => x.ReportHash).HasMaxLength(64);
        policyUatRun.Property(x => x.ExecutedBy).HasMaxLength(200);
        policyUatRun.HasIndex(x => new { x.PolicyVersion, x.Passed, x.ExecutedAt });
        policyUatRun.HasIndex(x => new { x.PolicyUatSuiteId, x.ExecutedAt });
        policyUatRun.HasOne<PolicyUatSuiteRecord>()
            .WithMany()
            .HasForeignKey(x => x.PolicyUatSuiteId)
            .OnDelete(DeleteBehavior.Restrict);

        var policyUatReceipt = modelBuilder.Entity<PolicyUatCommandReceiptRecord>();
        policyUatReceipt.ToTable(
            "PolicyUatCommandReceipt",
            "intg",
            table => table.HasCheckConstraint(
                "CK_PolicyUatCommandReceipt_Result",
                "([PolicyUatSuiteId] IS NOT NULL AND [PolicyUatRunId] IS NULL) OR ([PolicyUatSuiteId] IS NULL AND [PolicyUatRunId] IS NOT NULL)"));
        policyUatReceipt.HasKey(x => x.Id);
        policyUatReceipt.Property(x => x.ActorId).HasMaxLength(200);
        policyUatReceipt.Property(x => x.Operation).HasMaxLength(100);
        policyUatReceipt.Property(x => x.Key).HasMaxLength(200);
        policyUatReceipt.Property(x => x.RequestHash).HasMaxLength(64);
        policyUatReceipt.HasIndex(x => new { x.ActorId, x.Operation, x.Key }).IsUnique();
        policyUatReceipt.HasOne<PolicyUatSuiteRecord>()
            .WithMany()
            .HasForeignKey(x => x.PolicyUatSuiteId)
            .OnDelete(DeleteBehavior.Restrict);
        policyUatReceipt.HasOne<PolicyUatRunRecord>()
            .WithMany()
            .HasForeignKey(x => x.PolicyUatRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
