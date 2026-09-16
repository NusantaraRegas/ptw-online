namespace Ptw.Contracts;

public sealed record PrintPackageResponse(
    Guid Id,
    Guid PermitId,
    int PermitVersion,
    string RenderStatus,
    int Attempts,
    string? LastError,
    long? SizeBytes,
    string? Sha256,
    string PrintTemplateVersion,
    string CampaignAssetVersion,
    string Reference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? GeneratedAt,
    bool Downloadable);
