namespace Ptw.Contracts;

public sealed record AuthenticationOptionsResponse(bool DemoModeEnabled);

public sealed record DemoModeResponse(
    bool Enabled,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string ETag);
