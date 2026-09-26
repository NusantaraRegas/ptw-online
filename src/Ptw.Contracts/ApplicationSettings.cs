namespace Ptw.Contracts;

public sealed record AuthenticationOptionsResponse(bool DemoModeEnabled);

public sealed record DemoModeResponse(
    bool Enabled,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string ETag);

public sealed record UserGuideResponse(
    bool Available,
    string? FileName,
    long SizeBytes,
    string? Sha256,
    int Version,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string ETag);
