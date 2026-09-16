using System.Security.Cryptography;
using Ptw.Application;

namespace Ptw.Infrastructure;

public sealed class GeneratedDocumentSettings
{
    public bool Enabled { get; init; }
    public string StoragePath { get; init; } = string.Empty;
    /// <summary>Attempts after which a render is marked FAILED and needs an operator retry.</summary>
    public int MaxRenderAttempts { get; init; } = 5;
}

/// <summary>
/// Private storage for rendered permit packages. Generated documents live beside attachments on the same
/// protected volume but in their own root, so an official PDF can never be served through the attachment
/// path and vice versa.
/// </summary>
internal sealed class LocalGeneratedDocumentStorage : IGeneratedDocumentStorage
{
    private readonly string _root;

    public LocalGeneratedDocumentStorage(GeneratedDocumentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
        {
            throw new InvalidOperationException("GeneratedDocuments:StoragePath wajib dikonfigurasi.");
        }

        _root = Path.GetFullPath(settings.StoragePath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredGeneratedDocument> StoreAsync(
        Guid generatedDocumentId,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var storageKey = $"{generatedDocumentId:N}.pdf";
        var destination = Resolve(storageKey);
        var temporary = destination + ".render";

        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await output.WriteAsync(content, cancellationToken);
            }

            // Replace rather than move: a retry after a partial failure must overwrite cleanly.
            File.Move(temporary, destination, overwrite: true);
            return new StoredGeneratedDocument(
                storageKey,
                content.LongLength,
                Convert.ToHexString(SHA256.HashData(content)));
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
            throw new ResourceNotFoundException("Konten paket cetak", storageKey);
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
            || !string.Equals(Path.GetExtension(storageKey), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "print_package.storage_key_invalid",
                "Storage key paket cetak tidak valid.");
        }

        var resolved = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "print_package.storage_key_invalid",
                "Storage key paket cetak tidak valid.");
        }

        return resolved;
    }
}

internal sealed class DisabledGeneratedDocumentStorage : IGeneratedDocumentStorage
{
    private static InvalidRequestException Disabled() =>
        new("print_package.storage_disabled", "Penyimpanan paket cetak belum dikonfigurasi.");

    public Task<StoredGeneratedDocument> StoreAsync(
        Guid generatedDocumentId,
        byte[] content,
        CancellationToken cancellationToken) => throw Disabled();

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
        throw Disabled();

    public Task DeleteOrphanAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// Fail-closed default, mirroring <c>UnavailableMalwareScanner</c>. An environment without an approved
/// print template must never emit a document that looks like an official permit.
/// </summary>
internal sealed class UnavailablePrintPackageRenderer : IPrintPackageRenderer
{
    public bool IsAvailable => false;

    public PrintPackageRenderResult Render(PrintPackageRenderRequest request) =>
        throw new InvalidRequestException(
            "print_package.renderer_required",
            "Renderer paket cetak belum dikonfigurasi.");
}
