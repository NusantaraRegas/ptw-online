using System.Text.Json;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

/// <summary>
/// Read and operate on the official print packages produced at issuance. Rendering itself happens in the
/// Worker; this service only exposes status, authorised download, support retry and the draft preview.
/// </summary>
public sealed class PrintPackageService(
    IPermitStore permitStore,
    IPrintPackageStore printPackageStore,
    IGeneratedDocumentStorage storage,
    IPrintPackageRenderer renderer,
    IActorContext actorContext,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PagedResponse<PrintPackageResponse>> ListAsync(
        Guid permitId,
        CancellationToken cancellationToken)
    {
        await EnsureCanReadAsync(permitId, cancellationToken);
        var entries = await printPackageStore.ListForPermitAsync(permitId, cancellationToken);
        var items = entries.Select(Map).ToArray();
        return new PagedResponse<PrintPackageResponse>(items, items.Length);
    }

    public async Task<PrintPackageDownload> DownloadAsync(
        Guid permitId,
        Guid printPackageId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var permit = await EnsureCanReadAsync(permitId, cancellationToken);
        var entry = await printPackageStore.FindAsync(permitId, printPackageId, cancellationToken)
            ?? throw new ResourceNotFoundException("Paket cetak", printPackageId);

        // Only a completed render is the official document. Anything else would hand the field a sheet
        // that does not match the approved snapshot.
        if (!string.Equals(entry.RenderStatus, "READY", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(entry.StorageKey))
        {
            throw new InvalidRequestException(
                "print_package.not_ready",
                "Paket cetak resmi belum selesai dirender dan tidak dapat diunduh.");
        }

        var actor = actorContext.Current;
        await printPackageStore.RecordDownloadAsync(
            permitId,
            printPackageId,
            actor.Id,
            correlationId,
            clock.UtcNow,
            cancellationToken);

        var content = await storage.OpenReadAsync(entry.StorageKey, cancellationToken);
        var fileName = $"PTW-{permit.Permit.PermitNumber ?? permitId.ToString("N")}-v{entry.PermitVersion}.pdf";
        return new PrintPackageDownload(content, fileName, entry.MediaType, entry.SizeBytes ?? 0);
    }

    /// <summary>
    /// Requeues a render. Restricted to Administrators because it is a support action, and deliberately
    /// idempotent so a retried request does not stack attempts.
    /// </summary>
    public async Task<PrintPackageResponse> RetryAsync(
        Guid permitId,
        Guid printPackageId,
        CancellationToken cancellationToken)
    {
        await EnsureCanReadAsync(permitId, cancellationToken);
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException(
                "Hanya Administrator yang dapat menjalankan ulang render paket cetak.");
        }

        var entry = await printPackageStore.FindAsync(permitId, printPackageId, cancellationToken)
            ?? throw new ResourceNotFoundException("Paket cetak", printPackageId);
        await printPackageStore.RequestRetryAsync(entry.PrintPackageId, clock.UtcNow, cancellationToken);
        var refreshed = await printPackageStore.FindAsync(permitId, printPackageId, cancellationToken)
            ?? throw new ResourceNotFoundException("Paket cetak", printPackageId);
        return Map(refreshed);
    }

    /// <summary>
    /// Renders the current permit version as a watermarked preview. The preview is never persisted and is
    /// never an official document, so it cannot become closure evidence (FR-PRN-005).
    /// </summary>
    public async Task<PrintPackageDownload> PreviewAsync(Guid permitId, CancellationToken cancellationToken)
    {
        var stored = await EnsureCanReadAsync(permitId, cancellationToken);
        if (!renderer.IsAvailable)
        {
            throw new InvalidRequestException(
                "print_package.renderer_required",
                "Renderer paket cetak belum dikonfigurasi.");
        }

        var permit = stored.Permit;
        var snapshot = new PrintPackageSnapshotPayload(
            permit.Id,
            permit.PermitNumber,
            permit.Version,
            permit.Status.ToString(),
            permit.Draft,
            permit.HseValidation,
            permit.Approval,
            string.Empty,
            string.Empty,
            string.Empty,
            clock.UtcNow,
            AreaOperationsReview: permit.AreaOperationsReview);
        var result = renderer.Render(new PrintPackageRenderRequest(snapshot, "PREVIEW", Watermark: true));
        return new PrintPackageDownload(
            new MemoryStream(result.Content, writable: false),
            $"PRATINJAU-{permit.PermitNumber ?? permitId.ToString("N")}-v{permit.Version}.pdf",
            result.MediaType,
            result.Content.LongLength);
    }

    /// <summary>Deserializes an immutable snapshot for the render worker.</summary>
    public static PrintPackageSnapshotPayload ParseSnapshot(string snapshotJson) =>
        JsonSerializer.Deserialize<PrintPackageSnapshotPayload>(snapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Snapshot paket cetak tidak valid.");

    private async Task<StoredPermit> EnsureCanReadAsync(Guid permitId, CancellationToken cancellationToken)
    {
        var stored = await permitStore.FindAsync(permitId, cancellationToken)
            ?? throw new ResourceNotFoundException("Permit", permitId);
        var actor = actorContext.Current;
        if (!actor.LocationScopes.Contains("*")
            && !actor.LocationScopes.Contains(stored.Permit.Draft.LocationId))
        {
            throw new UnauthorizedAccessException("Lokasi PTW berada di luar cakupan otorisasi pengguna.");
        }

        // A Sponsor without a broader review role only ever sees their own permits.
        if (!actor.Roles.Overlaps(["Auditor", "Administrator", "HSEValidator", "AreaOwnerManager"])
            && !string.Equals(stored.Permit.Draft.SponsorId, actor.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("PTW berada di luar cakupan otorisasi pengguna.");
        }

        return stored;
    }

    private static PrintPackageResponse Map(PrintPackageDocumentEntry entry) => new(
        entry.PrintPackageId,
        entry.PermitId,
        entry.PermitVersion,
        entry.RenderStatus,
        entry.Attempts,
        entry.LastError,
        entry.SizeBytes,
        entry.Sha256,
        entry.PrintTemplateVersion,
        entry.CampaignAssetVersion,
        entry.SnapshotHash.Length >= 12 ? entry.SnapshotHash[..12] : entry.SnapshotHash,
        entry.CreatedAt,
        entry.GeneratedAt,
        string.Equals(entry.RenderStatus, "READY", StringComparison.Ordinal));
}
