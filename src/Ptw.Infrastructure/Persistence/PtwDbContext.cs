using Microsoft.EntityFrameworkCore;

namespace Ptw.Infrastructure.Persistence;

public sealed class PtwDbContext(DbContextOptions<PtwDbContext> options) : DbContext(options)
{
    public DbSet<PermitRecord> Permits => Set<PermitRecord>();
    public DbSet<PermitVersionRecord> PermitVersions => Set<PermitVersionRecord>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

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
    }
}
