using Ptw.Domain;

namespace Ptw.Application;

public sealed record SupportingDocumentEvidenceSnapshot(
    Guid AttachmentId,
    string DocumentCode,
    string FileName,
    string Sha256,
    string ScanStatus,
    string? DocumentNumber,
    string? DocumentRevision,
    DateTimeOffset? DocumentDate);

/// <summary>
/// Canonical content of an immutable <c>doc.PrintPackageSnapshot</c>. The renderer consumes only this
/// payload, never live permit tables, so a retry always reproduces the approved document (FSD ADR-010).
/// </summary>
public sealed record PrintPackageSnapshotPayload(
    Guid Id,
    string? PermitNumber,
    int PermitVersion,
    string Status,
    PermitDraft Permit,
    PermitValidationEvidence? HseValidation,
    PermitApprovalEvidence? Approval,
    string RuleVersion,
    string PrintTemplateVersion,
    string CampaignAssetVersion,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SupportingDocumentEvidenceSnapshot>? SupportingDocuments = null);

public sealed record PrintPackageDocumentEntry(
    Guid PrintPackageId,
    Guid PermitId,
    int PermitVersion,
    Guid GeneratedDocumentId,
    string RenderStatus,
    int Attempts,
    string? LastError,
    string? StorageKey,
    string MediaType,
    long? SizeBytes,
    string? Sha256,
    string PrintTemplateVersion,
    string CampaignAssetVersion,
    string SnapshotHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? GeneratedAt);

/// <summary>A claimed unit of render work. <see cref="Attempts"/> is the count before this attempt.</summary>
public sealed record PrintPackageRenderJob(
    Guid GeneratedDocumentId,
    Guid PrintPackageId,
    Guid PermitId,
    int PermitVersion,
    string SnapshotJson,
    string SnapshotHash,
    int Attempts);

public sealed record PrintPackageRenderRequest(
    PrintPackageSnapshotPayload Snapshot,
    string SnapshotHash,
    bool Watermark);

public sealed record PrintPackageRenderResult(
    byte[] Content,
    string MediaType,
    string RendererVersion);

public sealed record StoredGeneratedDocument(
    string StorageKey,
    long SizeBytes,
    string Sha256);

/// <summary>
/// Renders the official Nusantara Regas PTW form. Implementations must be deterministic for a given
/// snapshot so that render retries and golden-file regression produce identical bytes.
/// </summary>
public interface IPrintPackageRenderer
{
    bool IsAvailable { get; }

    PrintPackageRenderResult Render(PrintPackageRenderRequest request);
}

public interface IGeneratedDocumentStorage
{
    Task<StoredGeneratedDocument> StoreAsync(
        Guid generatedDocumentId,
        byte[] content,
        CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteOrphanAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IPrintPackageStore
{
    Task<IReadOnlyList<PrintPackageDocumentEntry>> ListForPermitAsync(
        Guid permitId,
        CancellationToken cancellationToken);
    Task<PrintPackageDocumentEntry?> FindAsync(
        Guid permitId,
        Guid printPackageId,
        CancellationToken cancellationToken);
    Task<string?> FindSnapshotJsonAsync(Guid printPackageId, CancellationToken cancellationToken);
    /// <summary>Queues a failed or ready package for another render attempt. Idempotent.</summary>
    Task<bool> RequestRetryAsync(
        Guid printPackageId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    /// <summary>
    /// Claims one due document using its row version, so competing worker instances cannot render twice.
    /// </summary>
    Task<PrintPackageRenderJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task CompleteRenderAsync(
        Guid generatedDocumentId,
        StoredGeneratedDocument stored,
        string mediaType,
        string rendererVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task FailRenderAsync(
        Guid generatedDocumentId,
        string failureReason,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken);
    /// <summary>
    /// Appends an append-only audit event for an official document download. BRD BR-AUD-001 treats
    /// sensitive downloads as material events, not just reads.
    /// </summary>
    Task RecordDownloadAsync(
        Guid permitId,
        Guid printPackageId,
        string actorId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record PrintPackageDownload(
    Stream Content,
    string FileName,
    string MediaType,
    long SizeBytes);
