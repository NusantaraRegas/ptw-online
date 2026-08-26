namespace Ptw.Contracts;

public sealed record PermitDraftRequest(
    string Title,
    string Description,
    string LocationId,
    string SponsorId,
    string PerformingAuthority,
    string Company,
    string PermitClass,
    string RiskLevel,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    string? ESimiExternalId,
    string? ESimiNumber,
    IReadOnlyList<string> Hazards,
    IReadOnlyList<string> Controls,
    IReadOnlyList<string> RequiredDocumentCodes);

public sealed record SubmitPermitRequest(
    bool ESimiEligible,
    bool RulesEvaluated,
    bool RequiredDocumentsSafe,
    IReadOnlyList<string> MissingRequirements);

public sealed record PermitResponse(
    Guid Id,
    string? PermitNumber,
    string Status,
    int Version,
    PermitDraftRequest Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ActiveWorkPeriodId,
    string? SuspensionReason,
    string ETag);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Count);

public sealed record MeResponse(
    string UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> LocationScopes,
    bool IsDevelopmentIdentity);
