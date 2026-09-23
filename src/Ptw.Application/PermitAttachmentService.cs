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
    IMalwareScanner malwareScanner,
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
        string category,
        string? supportingDocumentCode,
        string? documentNumber,
        string? documentRevision,
        DateTimeOffset? documentDate,
        Guid? printPackageId,
        Guid? supersedesAttachmentId,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        EnsureUploadAvailable();
        EnsureIdempotencyKey(idempotencyKey);
        if (declaredLength <= 0 || declaredLength > policy.MaxFileBytes)
        {
            throw new InvalidRequestException(
                "attachment.size_invalid",
                $"Ukuran file harus lebih dari 0 byte dan tidak melebihi {policy.MaxFileBytes} byte.");
        }

        var normalizedName = NormalizeFileName(fileName);
        var normalizedCategory = NormalizeCategory(category);
        var normalizedSupportingDocumentCode = NormalizeSupportingDocumentCode(
            normalizedCategory,
            supportingDocumentCode);
        EnsureMetadata(normalizedCategory, documentNumber, documentRevision, documentDate, printPackageId);

        var storedPermit = await GetOwnedPermitAsync(permitId, cancellationToken);
        EnsurePermitAllowsCategory(storedPermit.Permit, normalizedCategory);
        EnsureSupportingDocumentWasSelected(storedPermit.Permit, normalizedSupportingDocumentCode);
        var workingPermitVersion = storedPermit.Permit.Status == PermitStatus.RevisionRequired
            ? storedPermit.Permit.Version + 1
            : storedPermit.Permit.Version;
        var targetPermitVersion = workingPermitVersion;
        if (printPackageId is Guid packageId)
        {
            var package = await permitStore.FindPrintPackageAsync(packageId, cancellationToken);
            if (package is null || package.PermitId != permitId)
            {
                throw new InvalidRequestException(
                    "attachment.print_package_mismatch",
                    "PrintPackage tidak berasal dari PTW dan PermitVersion yang dipilih.");
            }


            targetPermitVersion = package.PermitVersion;
        }

        if (supersedesAttachmentId is Guid previousId)
        {
            var previous = await attachmentStore.FindActiveAsync(permitId, previousId, cancellationToken)
                ?? throw new ResourceNotFoundException("Lampiran yang digantikan", previousId);
            if (!string.Equals(previous.Category, normalizedCategory, StringComparison.Ordinal))
            {
                throw new InvalidRequestException(
                    "attachment.replacement_category_mismatch",
                    "Lampiran pengganti wajib memiliki kategori yang sama dengan file sebelumnya.");
            }
            if (!string.Equals(
                    previous.SupportingDocumentCode,
                    normalizedSupportingDocumentCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidRequestException(
                    "attachment.replacement_document_mismatch",
                    "Lampiran pengganti wajib merujuk jenis dokumen wajib atau Bagian 4 yang sama.");
            }
        }

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

            EnsureDetectedTypeMatches(normalizedName, declaredMediaType, storedContent.DetectedMediaType);
            var scan = await ScanAsync(storedContent, cancellationToken);

            var requestHash = Hash(new
            {
                PermitId = permitId,
                FileName = normalizedName,
                storedContent.SizeBytes,
                storedContent.Sha256
                ,
                Category = normalizedCategory
                ,
                SupportingDocumentCode = normalizedSupportingDocumentCode
                ,
                documentNumber
                ,
                documentRevision
                ,
                documentDate
                ,
                printPackageId
                ,
                supersedesAttachmentId
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

            var activeAttachments = await attachmentStore.ListActiveAsync(permitId, cancellationToken);
            if (activeAttachments.Count >= policy.MaxFilesPerPermit)
            {
                throw new InvalidRequestException(
                    "attachment.count_limit",
                    $"Jumlah lampiran aktif telah mencapai batas teknis {policy.MaxFilesPerPermit} file.");
            }

            var now = clock.UtcNow;
            if (normalizedCategory == "SIGNED_FIELD_COPY")
            {
                storedPermit.Permit.AddSignedFieldCopy(attachmentId, printPackageId!.Value, now);
            }
            else
            {
                storedPermit.Permit.AddAttachment(attachmentId, now);
            }
            var attachment = new PermitAttachmentEntry(
                attachmentId,
                permitId,
                workingPermitVersion,
                null,
                normalizedName,
                storedContent.SizeBytes,
                storedContent.DetectedMediaType,
                storedContent.Sha256,
                storedContent.StorageKey,
                scan.Status,
                scan.EvidenceReference,
                scan.ScannedAt,
                normalizedCategory,
                normalizedSupportingDocumentCode,
                NormalizeOptional(documentNumber),
                NormalizeOptional(documentRevision),
                documentDate?.ToUniversalTime(),
                targetPermitVersion,
                printPackageId,
                supersedesAttachmentId,
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
        EnsureFeatureEnabled();
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
        var workingPermitVersion = storedPermit.Permit.Status == PermitStatus.RevisionRequired
            ? storedPermit.Permit.Version + 1
            : storedPermit.Permit.Version;
        var removed = attachment with
        {
            RemovedInVersion = Math.Max(workingPermitVersion, attachment.AddedInVersion)
        };
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
        if (!string.Equals(attachment.ScanStatus, "CLEAN", StringComparison.Ordinal))
        {
            throw new InvalidRequestException(
                "attachment.not_clean",
                "Lampiran belum dinyatakan aman oleh malware scanner dan tidak dapat diunduh.");
        }

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
        EnsureFeatureEnabled();
        var stored = await permitStore.FindAsync(permitId, cancellationToken)
            ?? throw new ResourceNotFoundException("Permit", permitId);
        var actor = actorContext.Current;
        if (actor.Roles.Count == 0)
        {
            throw new UnauthorizedAccessException("Role pengguna diperlukan untuk membaca lampiran PTW.");
        }

        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        if (actor.Roles.Contains("Sponsor")
            && !actor.Roles.Overlaps(["HSEValidator", "AreaOwnerManager", "Administrator"])
            && !string.Equals(actor.Id, stored.Permit.Draft.SponsorId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Lampiran PTW berada di luar kepemilikan Sponsor.");
        }
    }

    private void EnsureFeatureEnabled()
    {
        if (!policy.Enabled)
        {
            throw new InvalidRequestException("attachment.disabled", "Fitur lampiran belum diaktifkan.");
        }
    }

    private void EnsureUploadAvailable()
    {
        EnsureFeatureEnabled();
        if (policy.RequireMalwareScan)
        {
            if (!malwareScanner.IsAvailable)
            {
                throw new InvalidRequestException(
                    "attachment.scanner_required",
                    "Upload dinonaktifkan sampai malware scanner production tersedia.");
            }
        }
    }

    private async Task<MalwareScanResult> ScanAsync(
        StoredAttachmentContent storedContent,
        CancellationToken cancellationToken)
    {
        if (!malwareScanner.IsAvailable)
        {
            return new MalwareScanResult("PENDING", null, null);
        }

        await using var scanContent = await storage.OpenReadAsync(storedContent.StorageKey, cancellationToken);
        var result = await malwareScanner.ScanAsync(
            scanContent,
            storedContent.DetectedMediaType,
            storedContent.Sha256,
            cancellationToken);
        var status = result.Status.Trim().ToUpperInvariant();
        if (status is not ("CLEAN" or "REJECTED"))
        {
            if (policy.RequireMalwareScan)
            {
                throw new InvalidRequestException(
                    "attachment.scan_inconclusive",
                    "Malware scanner tidak menghasilkan keputusan final yang dapat diverifikasi.");
            }

            return new MalwareScanResult("PENDING", null, null);
        }

        if (string.IsNullOrWhiteSpace(result.EvidenceReference) || result.ScannedAt is null)
        {
            throw new InvalidRequestException(
                "attachment.scan_evidence_missing",
                "Hasil malware scanner wajib menyertakan referensi evidence.");
        }

        return result with
        {
            Status = status,
            EvidenceReference = result.EvidenceReference.Trim(),
            ScannedAt = result.ScannedAt.Value.ToUniversalTime()
        };
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

    private static string NormalizeFileName(string fileName)
    {
        var normalized = Path.GetFileName(fileName).Trim();
        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 255
            || !new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "attachment.file_name_invalid",
                "Nama file harus valid, berakhiran PDF/JPEG/PNG, dan maksimum 255 karakter.");
        }

        return normalized;
    }

    private static string NormalizeCategory(string category)
    {
        var value = category.Trim().ToUpperInvariant();
        return value is "SUPPORTING" or "JSA" or "SIGNED_FIELD_COPY"
            ? value
            : throw new InvalidRequestException(
                "attachment.category_invalid",
                "Kategori lampiran harus SUPPORTING, JSA, atau SIGNED_FIELD_COPY.");
    }

    private static void EnsureMetadata(
        string category,
        string? documentNumber,
        string? documentRevision,
        DateTimeOffset? documentDate,
        Guid? printPackageId)
    {
        if (category is "JSA" or "SIGNED_FIELD_COPY"
            && (string.IsNullOrWhiteSpace(documentNumber)
                || string.IsNullOrWhiteSpace(documentRevision)
                || documentDate is null))
        {
            throw new InvalidRequestException(
                "attachment.document_metadata_required",
                "Nomor, revisi, dan tanggal dokumen wajib untuk JSA dan signed field copy.");
        }

        if (category == "SIGNED_FIELD_COPY" && printPackageId is null)
        {
            throw new InvalidRequestException(
                "attachment.print_package_required",
                "SIGNED_FIELD_COPY wajib merujuk PrintPackage yang digunakan di lapangan.");
        }
    }

    private static string? NormalizeSupportingDocumentCode(string category, string? submittedCode)
    {
        if (category == "SIGNED_FIELD_COPY")
        {
            if (!string.IsNullOrWhiteSpace(submittedCode))
            {
                throw new InvalidRequestException(
                    "attachment.supporting_document_not_allowed",
                    "Salinan lapangan tidak boleh dikaitkan dengan checklist Bagian 4.");
            }
            return null;
        }

        if (category == "JSA")
        {
            if (!string.IsNullOrWhiteSpace(submittedCode)
                && !string.Equals(
                    submittedCode.Trim(),
                    PermitSupportingDocumentCatalog.JsaCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidRequestException(
                    "attachment.jsa_document_code_invalid",
                    "Kategori JSA hanya boleh menggunakan kode dokumen JSA.");
            }
            return PermitSupportingDocumentCatalog.JsaCode;
        }

        if (string.IsNullOrWhiteSpace(submittedCode))
        {
            return null;
        }

        var mandatoryDocument = PermitMandatoryDocumentCatalog.Find(submittedCode);
        if (mandatoryDocument is not null)
        {
            if (mandatoryDocument.Code == PermitSupportingDocumentCatalog.JsaCode)
            {
                throw new InvalidRequestException(
                    "attachment.jsa_category_required",
                    "Dokumen JSA harus diunggah menggunakan kategori JSA.");
            }

            return mandatoryDocument.Code;
        }

        var option = PermitSupportingDocumentCatalog.Resolve(submittedCode);
        if (option.Required)
        {
            throw new InvalidRequestException(
                "attachment.jsa_category_required",
                "Dokumen JSA harus diunggah menggunakan kategori JSA.");
        }
        return option.Code;
    }

    private static void EnsureSupportingDocumentWasSelected(Permit permit, string? documentCode)
    {
        if (documentCode is null)
        {
            return;
        }

        if (PermitMandatoryDocumentCatalog.Find(documentCode) is not null)
        {
            return;
        }

        var selected = PermitSupportingDocumentCatalog.NormalizeAndValidate(
            permit.Draft.RequiredDocumentCodes,
            allowMissingRequired: true);
        if (!selected.Contains(documentCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "attachment.supporting_document_not_selected",
                "Pilih dokumen tersebut pada Bagian 4 dan simpan draft sebelum mengunggah file.");
        }
    }

    private static void EnsurePermitAllowsCategory(Permit permit, string category)
    {
        if (category == "SIGNED_FIELD_COPY")
        {
            if (permit.Status is not (
                PermitStatus.Issued
                or PermitStatus.Suspended
                or PermitStatus.Expired
                or PermitStatus.ClosureRequested))
            {
                throw new InvalidRequestException(
                    "attachment.signed_copy_state_invalid",
                    "SIGNED_FIELD_COPY hanya dapat diunggah untuk PTW Diterbitkan, Ditangguhkan, Kedaluwarsa, atau saat tindak lanjut penutupan diminta.");
            }

            if (permit.Status == PermitStatus.ClosureRequested
                && (permit.ClosureRequest is null
                    || string.IsNullOrWhiteSpace(permit.ClosureRequest.ReplacementReason)))
            {
                throw new InvalidRequestException(
                    "permit.closure.evidence_replacement_not_requested",
                    "Pemilik Wilayah belum meminta tindak lanjut atau salinan lapangan pengganti.");
            }
            return;
        }

        EnsurePermitEditable(permit);
    }

    private static void EnsureDetectedTypeMatches(
        string fileName,
        string declaredMediaType,
        string detectedMediaType)
    {
        var expectedByExtension = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => string.Empty
        };
        if (!string.Equals(expectedByExtension, detectedMediaType, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(declaredMediaType)
                && !string.Equals(declaredMediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(declaredMediaType, detectedMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "attachment.media_type_invalid",
                "Extension, MIME yang dinyatakan, dan signature file tidak konsisten.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
        value.ScanEvidenceReference,
        value.ScannedAt,
        value.Category,
        value.SupportingDocumentCode,
        value.DocumentNumber,
        value.DocumentRevision,
        value.DocumentDate,
        value.TargetPermitVersion,
        value.PrintPackageId,
        value.SupersedesAttachmentId,
        value.UploadedBy,
        value.UploadedAt);
}
