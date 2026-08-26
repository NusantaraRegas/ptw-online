namespace Ptw.Domain;

public sealed record DomainEvent(
    Guid Id,
    Guid PermitId,
    string Type,
    DateTimeOffset OccurredAt,
    object Payload);
