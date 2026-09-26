using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ptw.Contracts;

namespace Ptw.Application;

public sealed class UserGuideService(
    IUserGuideStore store,
    IAttachmentStorage storage,
    IMalwareScanner malwareScanner,
    IActorContext actorContext,
    IClock clock,
    AttachmentPolicy attachmentPolicy)
{
    private const string ReplaceOperation = "ReplaceUserGuide";

    // The guide is never shipped inside the build: the source documents are too large for
    // Git and live only in private attachment storage once an Administrator publishes them.
    // Until then the API reports an explicit "not available" state instead of a fallback.
    private static readonly UserGuideResponse NotAvailable =
        new(false, null, 0, null, 0, null, null, "\"0\"");

    public async Task<UserGuideResponse> GetAsync(CancellationToken cancellationToken)
    {
        var current = await store.GetCurrentAsync(cancellationToken);
        return current is null ? NotAvailable : Map(current);
    }

    public async Task<UserGuideDownload> DownloadAsync(CancellationToken cancellationToken)
    {
        var current = await store.GetCurrentAsync(cancellationToken)
            ?? throw new ResourceNotFoundException("Panduan pengguna", "aktif");

        var content = await storage.OpenReadAsync(current.StorageKey!, cancellationToken);
        return new UserGuideDownload(content, current.FileName, "application/pdf", current.SizeBytes);
    }

    public async Task<UserGuideResponse> ReplaceAsync(
        string fileName,
        string declaredMediaType,
        long declaredLength,
        Stream content,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        EnsureUploadAvailable();
        EnsureIdempotencyKey(idempotencyKey);
        var normalizedName = NormalizeFileName(fileName);
        if (!string.Equals(declaredMediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "user_guide.media_type_invalid",
                "Panduan pengguna wajib diunggah dengan media type application/pdf.");
        }
        if (declaredLength <= 0 || declaredLength > attachmentPolicy.MaxFileBytes)
        {
            throw new InvalidRequestException(
                "user_guide.size_invalid",
                $"Ukuran PDF harus lebih dari 0 byte dan tidak melebihi {attachmentPolicy.MaxFileBytes} byte.");
        }

        var guideId = Guid.CreateVersion7();
        StoredAttachmentContent? storedContent = null;
        try
        {
            storedContent = await storage.StoreAsync(
                guideId,
                content,
                attachmentPolicy.MaxFileBytes,
                cancellationToken);
            if (storedContent.SizeBytes != declaredLength)
            {
                throw new InvalidRequestException(
                    "user_guide.length_mismatch",
                    "Ukuran file yang diterima tidak sesuai dengan metadata upload.");
            }
            if (!string.Equals(storedContent.DetectedMediaType, "application/pdf", StringComparison.Ordinal))
            {
                throw new InvalidRequestException(
                    "user_guide.signature_invalid",
                    "Isi file bukan PDF yang valid.");
            }

            var scan = await ScanAsync(storedContent, cancellationToken);
            if (scan.Status != "CLEAN")
            {
                throw new InvalidRequestException(
                    "user_guide.not_clean",
                    "PDF panduan pengguna tidak dinyatakan aman dan tidak dapat diterbitkan.");
            }

            var requestHash = Hash(new
            {
                FileName = normalizedName,
                storedContent.SizeBytes,
                storedContent.Sha256
            });
            var prior = await store.FindCommandResultAsync(
                actor.Id,
                ReplaceOperation,
                idempotencyKey,
                requestHash,
                cancellationToken);
            if (prior is not null)
            {
                await storage.DeleteOrphanAsync(storedContent.StorageKey, cancellationToken);
                return Map(prior);
            }

            var now = clock.UtcNow;
            var replacement = new StoredUserGuide(
                guideId,
                normalizedName,
                storedContent.SizeBytes,
                storedContent.Sha256,
                storedContent.StorageKey,
                scan.EvidenceReference,
                scan.ScannedAt,
                0,
                now,
                actor.Id,
                string.Empty);
            var result = await store.ReplaceAsync(
                replacement,
                expectedETag,
                actor,
                correlationId,
                new UserGuideCommandContext(actor.Id, ReplaceOperation, idempotencyKey, requestHash),
                cancellationToken);
            return Map(result);
        }
        catch
        {
            if (storedContent is not null)
            {
                await storage.DeleteOrphanAsync(storedContent.StorageKey, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<MalwareScanResult> ScanAsync(
        StoredAttachmentContent content,
        CancellationToken cancellationToken)
    {
        await using var scanContent = await storage.OpenReadAsync(content.StorageKey, cancellationToken);
        var result = await malwareScanner.ScanAsync(
            scanContent,
            content.DetectedMediaType,
            content.Sha256,
            cancellationToken);
        var status = result.Status.Trim().ToUpperInvariant();
        if (status is not ("CLEAN" or "REJECTED")
            || string.IsNullOrWhiteSpace(result.EvidenceReference)
            || result.ScannedAt is null)
        {
            throw new InvalidRequestException(
                "user_guide.scan_inconclusive",
                "Malware scanner tidak menghasilkan keputusan final dengan evidence yang dapat diverifikasi.");
        }

        return result with
        {
            Status = status,
            EvidenceReference = result.EvidenceReference.Trim(),
            ScannedAt = result.ScannedAt.Value.ToUniversalTime()
        };
    }

    private void EnsureUploadAvailable()
    {
        if (!attachmentPolicy.Enabled)
        {
            throw new InvalidRequestException(
                "user_guide.upload_disabled",
                "Penyimpanan file belum diaktifkan.");
        }
        if (!malwareScanner.IsAvailable)
        {
            throw new InvalidRequestException(
                "attachment.scanner_required",
                "Upload dinonaktifkan sampai malware scanner production tersedia.");
        }
    }

    private Actor EnsureAdministrator()
    {
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException(
                "Peran Administrator diperlukan untuk mengganti panduan pengguna.");
        }
        return actor;
    }

    private static void EnsureIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib dan maksimum 200 karakter untuk mengganti panduan pengguna.");
        }
    }

    private static string NormalizeFileName(string fileName)
    {
        var normalized = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 255
            || normalized.Any(char.IsControl)
            || !string.Equals(Path.GetExtension(normalized), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "user_guide.file_name_invalid",
                "Nama file harus valid, berakhiran .pdf, dan maksimum 255 karakter.");
        }
        return normalized;
    }

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static UserGuideResponse Map(StoredUserGuide guide) => new(
        true,
        guide.FileName,
        guide.SizeBytes,
        guide.Sha256,
        guide.Version,
        guide.UpdatedAt,
        guide.UpdatedBy,
        guide.ETag);
}
