namespace Ptw.Infrastructure.Persistence;

public sealed class PermitRecord
{
    public Guid Id { get; set; }
    public string? PermitNumber { get; set; }
    public string Status { get; set; } = null!;
    public int Version { get; set; }
    public string LocationId { get; set; } = null!;
    public string SponsorId { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public string DraftJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? ActiveWorkPeriodId { get; set; }
    public string? SuspensionReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PermitVersionRecord
{
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public int Version { get; set; }
    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public sealed class AuditEventRecord
{
    public long Sequence { get; set; }
    public Guid Id { get; set; }
    public Guid PermitId { get; set; }
    public string EventType { get; set; } = null!;
    public string ActorId { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
}

public sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string ActorId { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid PermitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
