namespace Ptw.Application;

/// <summary>
/// Out-of-band password reset for a break-glass Administrator, used when no Administrator can log
/// in yet (first environment, seeded directory without credentials, or a lost password). It is
/// deliberately narrow: the account must be active and hold an approved, effective Administrator
/// assignment, the password must pass the normal policy, and the reset goes through the same store
/// path as the API so the audit event, outbox message, and session revocation are identical.
/// </summary>
public sealed class AdministratorPasswordBootstrap(
    IUserDirectoryStore store,
    LocalPasswordPolicy passwordPolicy,
    IClock clock)
{
    public const string ActorId = "system:bootstrap";

    public async Task<BootstrapResult> ResetAsync(string subjectId, string password, CancellationToken cancellationToken)
    {
        var normalizedSubjectId = subjectId.Trim();
        if (normalizedSubjectId.Length == 0)
        {
            throw new InvalidRequestException("bootstrap.subject_required", "Subject ID akun Administrator wajib diisi.");
        }

        var stored = await store.FindAsync(normalizedSubjectId, cancellationToken)
            ?? throw new ResourceNotFoundException("Pengguna", normalizedSubjectId);
        if (!stored.Account.IsActive)
        {
            throw new InvalidRequestException("bootstrap.account_inactive", "Akun nonaktif tidak dapat di-bootstrap.");
        }

        var identity = await store.ResolveIdentityAsync(normalizedSubjectId, clock.UtcNow, cancellationToken);
        if (identity is null || !identity.Roles.Contains("Administrator"))
        {
            throw new InvalidRequestException(
                "bootstrap.administrator_required",
                "Bootstrap hanya untuk akun dengan assignment Administrator yang disetujui dan berlaku.");
        }

        await passwordPolicy.ValidateAsync(password, stored.Account.UserName, cancellationToken);
        var actor = new Actor(
            ActorId,
            "Bootstrap",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var correlationId = $"bootstrap:{Guid.CreateVersion7():N}";
        var updated = await store.ResetPasswordAsync(
            normalizedSubjectId,
            password,
            stored.ETag,
            actor,
            correlationId,
            cancellationToken);
        return new BootstrapResult(updated.Account.SubjectId, updated.Account.UserName, correlationId);
    }
}

public sealed record BootstrapResult(string SubjectId, string UserName, string CorrelationId);
