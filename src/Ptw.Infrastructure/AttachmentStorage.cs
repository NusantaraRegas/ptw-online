using System.Security.Cryptography;
using Ptw.Application;

namespace Ptw.Infrastructure;

public sealed class AttachmentSettings
{
    public bool Enabled { get; init; }
    public long MaxFileBytes { get; init; }
    public int MaxFilesPerPermit { get; init; }
    public bool RequireMalwareScan { get; init; } = true;
    public string StoragePath { get; init; } = string.Empty;
}

internal sealed class LocalAttachmentStorage : IAttachmentStorage
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly string _root;

    public LocalAttachmentStorage(AttachmentSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
        {
            throw new InvalidOperationException("Attachments:StoragePath wajib dikonfigurasi.");
        }

        _root = Path.GetFullPath(settings.StoragePath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredAttachmentContent> StoreAsync(
        Guid attachmentId,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(_root, $"{attachmentId:N}.upload");
        long size = 0;
        var signature = new byte[PngSignature.Length];
        var signatureBytes = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > maxBytes)
                    {
                        throw new InvalidRequestException(
                            "attachment.size_invalid",
                            $"Ukuran file tidak boleh melebihi {maxBytes} byte.");
                    }

                    if (signatureBytes < signature.Length)
                    {
                        var copyLength = Math.Min(read, signature.Length - signatureBytes);
                        buffer.AsSpan(0, copyLength).CopyTo(signature.AsSpan(signatureBytes));
                        signatureBytes += copyLength;
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            var detected = DetectType(signature.AsSpan(0, signatureBytes));
            if (size == 0 || detected is null)
            {
                throw new InvalidRequestException(
                    "attachment.signature_invalid",
                    "Isi file tidak memiliki signature PDF, JPEG, atau PNG yang valid.");
            }

            var storageKey = $"{attachmentId:N}{detected.Value.Extension}";
            var destination = Resolve(storageKey);
            File.Move(temporary, destination);
            return new StoredAttachmentContent(
                storageKey,
                size,
                Convert.ToHexString(hash.GetHashAndReset()),
                detected.Value.MediaType);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (!File.Exists(path))
        {
            throw new ResourceNotFoundException("Konten lampiran", storageKey);
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteOrphanAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (!string.Equals(Path.GetFileName(storageKey), storageKey, StringComparison.Ordinal)
            || !new[] { ".pdf", ".jpg", ".png" }.Contains(
                Path.GetExtension(storageKey),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException("attachment.storage_key_invalid", "Storage key lampiran tidak valid.");
        }

        var resolved = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException("attachment.storage_key_invalid", "Storage key lampiran tidak valid.");
        }

        return resolved;
    }

    private static (string Extension, string MediaType)? DetectType(ReadOnlySpan<byte> signature)
    {
        if (signature.StartsWith(PdfSignature))
        {
            return (".pdf", "application/pdf");
        }

        if (signature.StartsWith(JpegSignature))
        {
            return (".jpg", "image/jpeg");
        }

        if (signature.StartsWith(PngSignature))
        {
            return (".png", "image/png");
        }

        return null;
    }
}

internal sealed class DisabledAttachmentStorage : IAttachmentStorage
{
    private static InvalidRequestException Disabled() =>
        new("attachment.disabled", "Fitur lampiran belum diaktifkan.");

    public Task<StoredAttachmentContent> StoreAsync(
        Guid attachmentId,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken) => throw Disabled();

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
        throw Disabled();

    public Task DeleteOrphanAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class UnavailableMalwareScanner : IMalwareScanner
{
    public bool IsAvailable => false;

    public Task<MalwareScanResult> ScanAsync(
        Stream content,
        string mediaType,
        string sha256,
        CancellationToken cancellationToken) =>
        throw new InvalidRequestException(
            "attachment.scanner_required",
            "Malware scanner belum dikonfigurasi.");
}

/// <summary>
/// Adapter used when <c>Attachments:RequireMalwareScan=false</c>: an upload that passed the
/// PDF/JPEG/PNG signature check is recorded as <c>CLEAN</c> without an external scanner. The evidence
/// reference names the mode explicitly so an auditor can tell these records from a real scan result.
/// </summary>
internal sealed class TrustedUploadScanner(IClock clock) : IMalwareScanner
{
    public const string EvidencePrefix = "trusted-upload:";

    public bool IsAvailable => true;

    public Task<MalwareScanResult> ScanAsync(
        Stream content,
        string mediaType,
        string sha256,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MalwareScanResult(
            "CLEAN",
            $"{EvidencePrefix}{sha256.ToLowerInvariant()}",
            clock.UtcNow));
}
