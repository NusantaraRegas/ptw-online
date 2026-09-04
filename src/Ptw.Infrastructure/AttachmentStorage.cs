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
        var storageKey = $"{attachmentId:N}.pdf";
        var destination = Resolve(storageKey);
        var temporary = destination + ".upload";
        long size = 0;
        var signature = new byte[PdfSignature.Length];
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
                            $"Ukuran PDF tidak boleh melebihi {maxBytes} byte.");
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

            if (size == 0 || !signature.AsSpan().SequenceEqual(PdfSignature))
            {
                throw new InvalidRequestException(
                    "attachment.pdf_signature_invalid",
                    "Isi file tidak memiliki signature PDF yang valid.");
            }

            File.Move(temporary, destination);
            return new StoredAttachmentContent(
                storageKey,
                size,
                Convert.ToHexString(hash.GetHashAndReset()),
                "application/pdf");
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
            || !storageKey.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
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
