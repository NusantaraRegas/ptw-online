namespace Ptw.Contracts;

public sealed record LoginRequest(string UserName, string Password);

public sealed record CreateUserRequest(
    string SubjectId,
    string UserName,
    string DisplayName,
    string? Position,
    string? Department,
    string Password);

public sealed record UpdateUserRequest(
    string DisplayName,
    string? Position,
    string? Department);

public sealed record SetUserActiveRequest(bool IsActive);

public sealed record ResetUserPasswordRequest(string Password);

public sealed record UserSignatureResponse(
    Guid Id,
    int Version,
    string MediaType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    bool IsActive);

public sealed record UserAccountResponse(
    string SubjectId,
    string UserName,
    string DisplayName,
    string? Position,
    string? Department,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LockedUntil,
    UserSignatureResponse? Signature,
    string ETag);

public sealed record LoginAuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string UserName,
    string? SubjectId,
    string DirectoryResult,
    string IdentitySource,
    string Outcome,
    string SourceAddress,
    string CorrelationId);
