namespace Ptw.Contracts;

public sealed record UserAuthorizationDraftRequest(
    string SubjectId,
    string RoleCode,
    IReadOnlyList<string> ActionCodes,
    Guid? LocationId,
    bool IncludeDescendants,
    IReadOnlyList<string> RequiredCompetencyCodes,
    string Kind,
    Guid? SourceAuthorizationId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);

public sealed record ReturnAuthorizationForChangesRequest(string Reason);

public sealed record UserAuthorizationResponse(
    Guid Id,
    string SubjectId,
    string RoleCode,
    IReadOnlyList<string> ActionCodes,
    Guid? LocationId,
    bool IncludeDescendants,
    IReadOnlyList<string> RequiredCompetencyCodes,
    string Kind,
    Guid? SourceAuthorizationId,
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
