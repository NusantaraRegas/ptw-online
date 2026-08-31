namespace Ptw.Contracts;

public sealed record LocationDraftRequest(
    string Code,
    string Name,
    Guid? ParentId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);

public sealed record ReturnLocationForChangesRequest(string Reason);

public sealed record LocationMasterResponse(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    string Status,
    bool IsEffective,
    int Version,
    string MakerId,
    string? CheckerId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ETag);
