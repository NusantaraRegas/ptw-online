using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed record PermitAttachmentDownload(
    Stream Content,
    string FileName,
    string MediaType,
    long SizeBytes);

public sealed class PermitAttachmentService(
    IPermitStore permitStore,
    IPermitAttachmentStore attachmentStore,
    IAttachmentStorage storage,
    IActorContext actorContext,
    IClock clock,
    AttachmentPolicy policy)
{
    private const string AddOperation = "AddPermitAttachment";
    private const string RemoveOperation = "RemovePermitAttachment";

    public async Task<IReadOnlyList<PermitAttachmentResponse>> ListAsync(
        Guid permitId,
        CancellationToken cancellationToken)
    {
        await EnsureCanReadPermitAsync(permitId, cancellationToken);
        return (await attachmentStore.ListActiveAsync(permitId, cancellationToken))
            .Select(ToResponse)
            .ToArray();
    }

    public async Task<PermitAttachmentMutationResponse> UploadAsync(
        Guid permitId,
        string fileName,
        string declaredMediaType,
        long declaredLength,
        Stream content,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureFeatureAvailable();
        EnsureIdempotencyKey(idempotencyKey);
        if (declaredLength <= 0 || declaredLength > policy.MaxFileBytes)
        {
            throw new InvalidRequestException(
                "attachment.size_invalid",
                $"Ukuran PDF harus lebih dari 0 byte dan tidak melebihi {policy.MaxFileBytes} byte.");
        }

        var normalizedName = NormalizePdfFileName(fileName);
        if (!string.IsNullOrWhiteSpace(declaredMediaType)
            && !string.Equals(declaredMediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declaredMediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException("attachment.media_type_invalid", "Hanya file PDF yang dapat diunggah.");
        }

        var storedPermit = await GetOwnedPermitAsync(permitId, cancellationToken);

        var attachmentId = Guid.CreateVersion7();
        StoredAttachmentContent? storedContent = null;
        try
        {
            storedContent = await storage.StoreAsync(
                attachmentId,
                content,
                policy.MaxFileBytes,
                cancellationToken);
            if (storedContent.SizeBytes != declaredLength)
            {
                throw new InvalidRequestException(
                    "attachment.length_mismatch",
                    "Ukuran file yang diterima tidak sesuai dengan metadata upload.");
            }

            var requestHash = Hash(new
            {
                PermitId = permitId,
                FileName = normalizedName,
                storedContent.SizeBytes,
                storedContent.Sha256
            });
            var actor = actorContext.Current;
            var prior = await attachmentStore.FindIdempotentResultAsync(
                actor.Id,
                AddOperation,
                idempotencyKey,
                requestHash,
                cancellationToken);
            if (prior is not null)
            {
                await storage.DeleteOrphanAsync(storedContent.StorageKey, cancellationToken);
                return ToMutationResponse(prior);
            }

            EnsurePermitEditable(storedPermit.Permit);
            var activeAttachments = await attachmentStore.ListActiveAsync(permitId, cancellationToken);
            if (activeAttachments.Count >= policy.MaxFilesPerPermit)
            {
                throw new InvalidRequestException(
                    "attachment.count_limit",
                    $"Jumlah lampiran aktif telah mencapai batas teknis {policy.MaxFilesPerPermit} file.");
            }

            var now = clock.UtcNow;
            storedPermit.Permit.AddAttachment(attachmentId, now);
            var attachment = new PermitAttachmentEntry(
                attachmentId,
                permitId,
                storedPermit.Permit.Version,
                null,
                normalizedName,
                storedContent.SizeBytes,
                storedContent.DetectedMediaType,
                storedContent.Sha256,
                storedContent.StorageKey,
                "NOT_SCANNED",
                actor.Id,
                now);
            var result = await attachmentStore.AddAsync(
                storedPermit.Permit,
                attachment,
                expectedETag,
                actor,
                correlationId,
                new IdempotencyContext(actor.Id, AddOperation, idempotencyKey, requestHash),
                cancellationToken);
            return ToMutationResponse(result);
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

    public async Task<PermitAttachmentMutationResponse> RemoveAsync(
        Guid permitId,
        Guid attachmentId,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureFeatureAvailable();
        EnsureIdempotencyKey(idempotencyKey);
        var actor = actorContext.Current;
        var storedPermit = await GetOwnedPermitAsync(permitId, cancellationToken);
        var requestHash = Hash(new { PermitId = permitId, AttachmentId = attachmentId });
        var prior = await attachmentStore.FindIdempotentResultAsync(
            actor.Id,
            RemoveOperation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return ToMutationResponse(prior);
        }

        EnsurePermitEditable(storedPermit.Permit);
        var attachment = await attachmentStore.FindActiveAsync(permitId, attachmentId, cancellationToken)
            ?? throw new ResourceNotFoundException("Lampiran", attachmentId);
        storedPermit.Permit.RemoveAttachment(attachmentId, clock.UtcNow);
        var removed = attachment with { RemovedInVersion = storedPermit.Permit.Version };
        var result = await attachmentStore.RemoveAsync(
            storedPermit.Permit,
            removed,
            expectedETag,
            actor,
            correlationId,
            new IdempotencyContext(actor.Id, RemoveOperation, idempotencyKey, requestHash),
            cancellationToken);
        return ToMutationResponse(result);
    }

    public async Task<PermitAttachmentDownload> DownloadAsync(
        Guid permitId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        await EnsureCanReadPermitAsync(permitId, cancellationToken);
        var attachment = await attachmentStore.FindActiveAsync(permitId, attachmentId, cancellationToken)
            ?? throw new ResourceNotFoundException("Lampiran", attachmentId);
        var content = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        return new PermitAttachmentDownload(
            content,
            attachment.FileName,
            attachment.MediaType,
            attachment.SizeBytes);
    }

    private async Task<StoredPermit> GetOwnedPermitAsync(
        Guid permitId,
        CancellationToken cancellationToken)
    {
        var stored = await permitStore.FindAsync(permitId, cancellationToken)
            ?? throw new ResourceNotFoundException("Permit", permitId);
        var actor = actorContext.Current;
        if (!actor.Roles.Overlaps(["Sponsor", "Administrator"]))
        {
            throw new UnauthorizedAccessException("Peran Sponsor diperlukan untuk mengelola lampiran PTW.");
        }

        if (!string.Equals(actor.Id, stored.Permit.Draft.SponsorId, StringComparison.OrdinalIgnoreCase)
            && !actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Lampiran PTW berada di luar kepemilikan Sponsor.");
        }

        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        return stored;
    }

    private static void EnsurePermitEditable(Permit permit)
    {
        if (permit.Status is not (PermitStatus.Draft or PermitStatus.RevisionRequired))
        {
            throw new InvalidRequestException(
                "attachment.permit_not_editable",
                "Lampiran hanya dapat diubah saat PTW berstatus DRAFT atau REVISION_REQUIRED.");
        }
    }

    private async Task EnsureCanReadPermitAsync(Guid permitId, CancellationToken cancellationToken)
    {
        EnsureFeatureAvailable();
        var stored = await permitStore.FindAsync(permitId, cancellationToken)
            ?? throw new ResourceNotFoundException("Permit", permitId);
        var actor = actorContext.Current;
        if (actor.Roles.Count == 0)
        {
            throw new UnauthorizedAccessException("Role pengguna diperlukan untuk membaca lampiran PTW.");
        }

        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
    }

    private void EnsureFeatureAvailable()
    {
        if (!policy.Enabled)
        {
            throw new InvalidRequestException("attachment.disabled", "Fitur lampiran belum diaktifkan.");
        }

        if (policy.RequireMalwareScan)
        {
            throw new InvalidRequestException(
                "attachment.scanner_required",
                "Upload dinonaktifkan sampai malware scanner production tersedia.");
        }
    }

    private static void EnsureIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib dan maksimum 200 karakter untuk command lampiran.");
        }
    }

    private static void EnsureLocationScope(Actor actor, string locationId)
    {
        if (!actor.LocationScopes.Contains("*") && !actor.LocationScopes.Contains(locationId))
        {
            throw new UnauthorizedAccessException("PTW berada di luar cakupan lokasi actor.");
        }
    }

    private static string NormalizePdfFileName(string fileName)
    {
        var normalized = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255
            || !string.Equals(Path.GetExtension(normalized), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "attachment.file_name_invalid",
                "Nama file harus valid, berakhiran .pdf, dan maksimum 255 karakter.");
        }

        return normalized;
    }

    private static string Hash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static PermitAttachmentMutationResponse ToMutationResponse(StoredPermitAttachment value) =>
        new(ToResponse(value.Attachment), value.Permit.Permit.Version, value.Permit.ETag);

    private static PermitAttachmentResponse ToResponse(PermitAttachmentEntry value) => new(
        value.Id,
        value.PermitId,
        value.AddedInVersion,
        value.RemovedInVersion,
        value.FileName,
        value.SizeBytes,
        value.MediaType,
        value.Sha256,
        value.ScanStatus,
        value.UploadedBy,
        value.UploadedAt);
}
