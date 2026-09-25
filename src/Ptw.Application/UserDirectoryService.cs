using System.Buffers.Binary;
using System.Security.Cryptography;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class UserDirectoryService(
    IUserDirectoryStore store,
    ILoginAuditStore loginAudit,
    IBreachedPasswordChecker breachedPasswords,
    IActorContext actorContext,
    IClock clock)
{
    private const int MaxLoginEvents = 500;
    private const int MaxSignatureBytes = 256 * 1024;
    private const int MaxSignatureDimension = 2000;

    public async Task<PagedResponse<UserAccountResponse>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        var users = await store.ListAsync(cancellationToken);
        return new(users.Select(Map).ToArray(), users.Count);
    }

    public async Task<UserAccountResponse> GetAsync(string subjectId, CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        return Map(await RequiredAsync(subjectId, cancellationToken));
    }

    public async Task<UserSignatureEntry> GetActiveSignatureAsync(
        string subjectId,
        CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        _ = await RequiredAsync(subjectId, cancellationToken);
        return await store.FindActiveSignatureAsync(subjectId, cancellationToken)
            ?? throw new ResourceNotFoundException("Tanda tangan pengguna", subjectId);
    }

    public async Task<UserAccountResponse> CreateAsync(
        CreateUserRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        await ValidatePasswordAsync(request.Password, request.UserName, cancellationToken);
        var account = UserAccount.Create(
            request.SubjectId,
            request.UserName,
            request.DisplayName,
            request.Position,
            request.Department,
            clock.UtcNow);
        return Map(await store.AddAsync(account, request.Password, actor, correlationId, cancellationToken));
    }

    public async Task<UserAccountResponse> UpdateAsync(
        string subjectId,
        UpdateUserRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        var stored = await RequiredAsync(subjectId, cancellationToken);
        stored.Account.Update(request.DisplayName, request.Position, request.Department, clock.UtcNow);
        return Map(await store.UpdateAsync(
            stored.Account,
            expectedETag,
            actor,
            "user_profile_updated",
            correlationId,
            cancellationToken));
    }

    public async Task<UserAccountResponse> SetActiveAsync(
        string subjectId,
        SetUserActiveRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        if (string.Equals(actor.Id, subjectId, StringComparison.OrdinalIgnoreCase) && !request.IsActive)
        {
            throw new InvalidRequestException("user.self_deactivation_forbidden", "Administrator tidak dapat menonaktifkan akunnya sendiri.");
        }

        var stored = await RequiredAsync(subjectId, cancellationToken);
        stored.Account.SetActive(request.IsActive, clock.UtcNow);
        return Map(await store.UpdateAsync(
            stored.Account,
            expectedETag,
            actor,
            request.IsActive ? "user_activated" : "user_deactivated",
            correlationId,
            cancellationToken));
    }

    public async Task<UserAccountResponse> ResetPasswordAsync(
        string subjectId,
        ResetUserPasswordRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        var stored = await RequiredAsync(subjectId, cancellationToken);
        await ValidatePasswordAsync(request.Password, stored.Account.UserName, cancellationToken);
        return Map(await store.ResetPasswordAsync(
            subjectId,
            request.Password,
            expectedETag,
            actor,
            correlationId,
            cancellationToken));
    }

    /// <summary>Ends every live session of the account ("log out everywhere").</summary>
    public async Task<UserAccountResponse> RevokeSessionsAsync(
        string subjectId,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        var stored = await RequiredAsync(subjectId, cancellationToken);
        stored.Account.RevokeSessions(clock.UtcNow);
        return Map(await store.RevokeSessionsAsync(stored.Account, expectedETag, actor, correlationId, cancellationToken));
    }

    /// <summary>Recent login journal (newest first) so lockouts and unexpected paths are visible to Administrators.</summary>
    public async Task<PagedResponse<LoginAuditEventResponse>> ListLoginEventsAsync(
        int? limit,
        string? subjectId,
        CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        var effectiveLimit = Math.Clamp(limit ?? 100, 1, MaxLoginEvents);
        var events = await loginAudit.ListRecentAsync(
            effectiveLimit,
            string.IsNullOrWhiteSpace(subjectId) ? null : subjectId.Trim(),
            cancellationToken);
        var items = events.Select(x => new LoginAuditEventResponse(
            x.Id,
            x.OccurredAt,
            x.UserName,
            x.SubjectId,
            x.DirectoryResult,
            x.IdentitySource,
            x.Outcome,
            x.SourceAddress,
            x.CorrelationId)).ToArray();
        return new(items, items.Length);
    }

    public async Task<UserAccountResponse> UploadSignatureAsync(
        string subjectId,
        string mediaType,
        long sizeBytes,
        Stream content,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        if (!string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException("signature.png_required", "Tanda tangan wajib berupa PNG.");
        }

        if (sizeBytes <= 0 || sizeBytes > MaxSignatureBytes)
        {
            throw new InvalidRequestException("signature.size_invalid", "Ukuran PNG tanda tangan maksimum 256 KB.");
        }

        await using var buffer = new MemoryStream((int)sizeBytes);
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        ValidatePng(bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        return Map(await store.AddSignatureAsync(
            subjectId,
            "image/png",
            bytes,
            sha256,
            expectedETag,
            actor,
            correlationId,
            cancellationToken));
    }

    private static void ValidatePng(byte[] content)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.Length < 24 || !content.AsSpan(0, 8).SequenceEqual(signature))
        {
            throw new InvalidRequestException("signature.content_invalid", "Isi file bukan PNG yang valid.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(20, 4));
        if (width <= 0 || height <= 0 || width > MaxSignatureDimension || height > MaxSignatureDimension)
        {
            throw new InvalidRequestException("signature.dimension_invalid", "Dimensi PNG tanda tangan maksimum 2000 x 2000 piksel.");
        }
    }

    private async Task<StoredUserAccount> RequiredAsync(string subjectId, CancellationToken cancellationToken) =>
        await store.FindAsync(subjectId, cancellationToken)
        ?? throw new ResourceNotFoundException("Pengguna", subjectId);

    private Actor EnsureAdministrator()
    {
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Peran Administrator diperlukan untuk mengelola pengguna.");
        }

        return actor;
    }

    private async Task ValidatePasswordAsync(string password, string userName, CancellationToken cancellationToken)
    {
        if (password.Length < 12 || password.Length > 128
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit))
        {
            throw new InvalidRequestException(
                "user.password_weak",
                "Password harus 12-128 karakter dan memuat huruf besar, huruf kecil, serta angka.");
        }

        var normalizedUserName = userName.Trim();
        if (normalizedUserName.Length >= 4
            && password.Contains(normalizedUserName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "user.password_contains_username",
                "Password tidak boleh memuat username.");
        }

        // Local passwords exist for break-glass Administrator access, so a password that already
        // circulates in breach corpora is refused even when it satisfies the composition rule.
        if (await breachedPasswords.IsBreachedAsync(password, cancellationToken))
        {
            throw new InvalidRequestException(
                "user.password_breached",
                "Password ini ditemukan pada daftar kebocoran data publik. Gunakan password lain.");
        }
    }

    private static UserAccountResponse Map(StoredUserAccount stored) => new(
        stored.Account.SubjectId,
        stored.Account.UserName,
        stored.Account.DisplayName,
        stored.Account.Position,
        stored.Account.Department,
        stored.Account.IsActive,
        stored.Account.Version,
        stored.Account.CreatedAt,
        stored.Account.UpdatedAt,
        stored.LockedUntil,
        stored.Signature is null ? null : new UserSignatureResponse(
            stored.Signature.Id,
            stored.Signature.Version,
            stored.Signature.MediaType,
            stored.Signature.Content.LongLength,
            stored.Signature.Sha256,
            stored.Signature.UploadedAt,
            stored.Signature.UploadedBy,
            stored.Signature.IsActive),
        stored.ETag);
}
