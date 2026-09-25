using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ptw.Application;
using Ptw.Domain;

namespace Ptw.Infrastructure.Persistence;

public sealed class UserDirectoryStore(PtwDbContext dbContext, IClock clock) : IUserDirectoryStore
{
    private const int PasswordIterations = 210_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StoredUserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.UserAccounts.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        var signatures = await dbContext.UserSignatureVersions.AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.SubjectId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var now = clock.UtcNow;
        var lockedUntil = await dbContext.UserCredentials.AsNoTracking()
            .Where(x => x.LockedUntil != null && x.LockedUntil > now)
            .ToDictionaryAsync(x => x.SubjectId, x => x.LockedUntil, StringComparer.OrdinalIgnoreCase, cancellationToken);
        return accounts.Select(account => ToStored(
            account,
            signatures.GetValueOrDefault(account.SubjectId),
            lockedUntil.GetValueOrDefault(account.SubjectId))).ToArray();
    }

    public async Task<StoredUserAccount?> FindAsync(string subjectId, CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == subjectId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == subjectId && x.IsActive, cancellationToken);
        return ToStored(account, signature, await ActiveLockAsync(subjectId, cancellationToken));
    }

    public async Task<StoredUserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalized = NormalizeUserName(userName);
        var account = await dbContext.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalized, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == account.SubjectId && x.IsActive, cancellationToken);
        return ToStored(account, signature);
    }

    public async Task<LocalAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUserName(userName);
        var account = await dbContext.UserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalized, cancellationToken);
        if (account is null)
        {
            return new LocalAuthenticationResult(LocalAuthenticationOutcome.UnknownUser, null, null);
        }

        if (!account.IsActive)
        {
            return new LocalAuthenticationResult(LocalAuthenticationOutcome.AccountInactive, ToStored(account, null), null);
        }

        var credential = await dbContext.UserCredentials
            .SingleOrDefaultAsync(x => x.SubjectId == account.SubjectId, cancellationToken);
        if (credential is null)
        {
            return new LocalAuthenticationResult(LocalAuthenticationOutcome.InvalidPassword, ToStored(account, null), null);
        }

        var now = clock.UtcNow;
        if (credential.LockedUntil > now)
        {
            return new LocalAuthenticationResult(
                LocalAuthenticationOutcome.LockedOut,
                ToStored(account, null, credential.LockedUntil),
                credential.LockedUntil);
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            password,
            credential.PasswordSalt,
            credential.Iterations,
            HashAlgorithmName.SHA256,
            credential.PasswordHash.Length);
        if (!CryptographicOperations.FixedTimeEquals(candidate, credential.PasswordHash))
        {
            credential.FailedAttempts++;
            var lockoutTriggered = credential.FailedAttempts >= 5;
            if (lockoutTriggered)
            {
                credential.LockedUntil = now.AddMinutes(5);
                credential.FailedAttempts = 0;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new LocalAuthenticationResult(
                lockoutTriggered ? LocalAuthenticationOutcome.LockoutTriggered : LocalAuthenticationOutcome.InvalidPassword,
                ToStored(account, null, credential.LockedUntil),
                credential.LockedUntil);
        }

        credential.FailedAttempts = 0;
        credential.LockedUntil = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == account.SubjectId && x.IsActive, cancellationToken);
        return new LocalAuthenticationResult(LocalAuthenticationOutcome.Succeeded, ToStored(account, signature), null);
    }

    public async Task<ResolvedUserIdentity?> ResolveIdentityAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == subjectId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var utc = now.ToUniversalTime();
        var assignments = await dbContext.UserAuthorizations.AsNoTracking()
            .Where(x => x.SubjectId == subjectId
                && x.Status == "Approved"
                && x.EffectiveFrom <= utc
                && (x.EffectiveUntil == null || utc < x.EffectiveUntil))
            .ToListAsync(cancellationToken);
        var locationIds = assignments.Where(x => x.LocationId != null)
            .Select(x => x.LocationId!.Value)
            .ToHashSet();
        var locations = locationIds.Count == 0
            ? []
            : await dbContext.LocationMasters.AsNoTracking()
                .Where(x => x.Status == "Approved" && x.EffectiveFrom <= utc
                    && (x.EffectiveUntil == null || utc < x.EffectiveUntil))
                .ToListAsync(cancellationToken);

        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
            if (assignment.LocationId is null)
            {
                scopes.Add("*");
                continue;
            }

            var root = locations.SingleOrDefault(x => x.Id == assignment.LocationId);
            if (root is null)
            {
                continue;
            }

            scopes.Add(root.Code);
            if (assignment.IncludeDescendants)
            {
                AddDescendants(root.Id, locations, scopes);
            }
        }

        return new ResolvedUserIdentity(
            account.SubjectId,
            account.DisplayName,
            account.IsActive,
            account.SecurityStamp,
            assignments.Select(x => x.RoleCode).ToHashSet(StringComparer.OrdinalIgnoreCase),
            scopes,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public async Task<UserSignatureEntry?> FindActiveSignatureAsync(
        string subjectId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == subjectId && x.IsActive, cancellationToken);
        return record is null ? null : ToEntry(record);
    }

    public async Task<StoredUserAccount> AddAsync(
        UserAccount account,
        string password,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.UserAccounts.AnyAsync(
                x => x.SubjectId == account.SubjectId || x.NormalizedUserName == NormalizeUserName(account.UserName),
                cancellationToken))
        {
            throw new InvalidRequestException("user.duplicate", "Subject ID atau username sudah digunakan.");
        }

        var record = ToRecord(account);
        var (salt, hash) = HashPassword(password);
        dbContext.UserAccounts.Add(record);
        dbContext.UserCredentials.Add(new UserCredentialRecord
        {
            SubjectId = account.SubjectId,
            PasswordSalt = salt,
            PasswordHash = hash,
            Iterations = PasswordIterations,
            PasswordChangedAt = clock.UtcNow
        });
        AddEvidence(account.SubjectId, "user_created", actor.Id, correlationId, new
        {
            account.SubjectId,
            account.UserName,
            account.DisplayName,
            account.Position,
            account.Department
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(record, null);
    }

    public async Task<StoredUserAccount> UpdateAsync(
        UserAccount account,
        string expectedETag,
        Actor actor,
        string eventType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.UserAccounts.SingleOrDefaultAsync(
            x => x.SubjectId == account.SubjectId,
            cancellationToken) ?? throw new ResourceNotFoundException("Pengguna", account.SubjectId);
        ApplyConcurrency(record, expectedETag);
        Apply(record, account);
        AddEvidence(account.SubjectId, eventType, actor.Id, correlationId, new
        {
            account.SubjectId,
            account.DisplayName,
            account.Position,
            account.Department,
            account.IsActive,
            account.Version
        });
        await SaveConcurrencySafeAsync(cancellationToken);
        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == account.SubjectId && x.IsActive, cancellationToken);
        return ToStored(record, signature);
    }

    public async Task<StoredUserAccount> ResetPasswordAsync(
        string subjectId,
        string password,
        string expectedETag,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.UserAccounts.SingleOrDefaultAsync(x => x.SubjectId == subjectId, cancellationToken)
            ?? throw new ResourceNotFoundException("Pengguna", subjectId);
        ApplyConcurrency(record, expectedETag);
        var credential = await dbContext.UserCredentials.SingleAsync(x => x.SubjectId == subjectId, cancellationToken);
        var (salt, hash) = HashPassword(password);
        credential.PasswordSalt = salt;
        credential.PasswordHash = hash;
        credential.Iterations = PasswordIterations;
        credential.FailedAttempts = 0;
        credential.LockedUntil = null;
        credential.PasswordChangedAt = clock.UtcNow;
        record.Version++;
        record.UpdatedAt = clock.UtcNow;
        // A new password ends every session that was opened with the old one.
        record.SecurityStamp = UserAccount.NewSecurityStamp();
        AddEvidence(subjectId, "user_password_reset", actor.Id, correlationId, new { SubjectId = subjectId, record.Version });
        await SaveConcurrencySafeAsync(cancellationToken);
        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == subjectId && x.IsActive, cancellationToken);
        return ToStored(record, signature);
    }

    public async Task<StoredUserAccount> RevokeSessionsAsync(
        UserAccount account,
        string expectedETag,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.UserAccounts.SingleOrDefaultAsync(
            x => x.SubjectId == account.SubjectId,
            cancellationToken) ?? throw new ResourceNotFoundException("Pengguna", account.SubjectId);
        ApplyConcurrency(record, expectedETag);
        Apply(record, account);
        AddEvidence(account.SubjectId, "user_sessions_revoked", actor.Id, correlationId, new
        {
            account.SubjectId,
            account.Version
        });
        await SaveConcurrencySafeAsync(cancellationToken);
        var signature = await dbContext.UserSignatureVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == account.SubjectId && x.IsActive, cancellationToken);
        return ToStored(record, signature, await ActiveLockAsync(account.SubjectId, cancellationToken));
    }

    public async Task<StoredUserAccount> AddSignatureAsync(
        string subjectId,
        string mediaType,
        byte[] content,
        string sha256,
        string expectedETag,
        Actor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts.SingleOrDefaultAsync(x => x.SubjectId == subjectId, cancellationToken)
            ?? throw new ResourceNotFoundException("Pengguna", subjectId);
        ApplyConcurrency(account, expectedETag);
        var current = await dbContext.UserSignatureVersions
            .SingleOrDefaultAsync(x => x.SubjectId == subjectId && x.IsActive, cancellationToken);
        if (current is not null)
        {
            current.IsActive = false;
            current.SupersededAt = clock.UtcNow;
        }

        var nextVersion = await dbContext.UserSignatureVersions
            .Where(x => x.SubjectId == subjectId)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken) ?? 0;
        var signature = new UserSignatureVersionRecord
        {
            Id = Guid.CreateVersion7(),
            SubjectId = subjectId,
            Version = nextVersion + 1,
            MediaType = mediaType,
            Content = content,
            Sha256 = sha256,
            UploadedBy = actor.Id,
            UploadedAt = clock.UtcNow,
            IsActive = true
        };
        dbContext.UserSignatureVersions.Add(signature);
        account.Version++;
        account.UpdatedAt = clock.UtcNow;
        AddEvidence(subjectId, "user_signature_replaced", actor.Id, correlationId, new
        {
            SubjectId = subjectId,
            SignatureId = signature.Id,
            SignatureVersion = signature.Version,
            signature.Sha256,
            AccountVersion = account.Version
        });
        await SaveConcurrencySafeAsync(cancellationToken);
        return ToStored(account, signature);
    }

    private void AddEvidence(string subjectId, string eventType, string actorId, string correlationId, object payload)
    {
        var aggregateId = DeterministicGuid(subjectId);
        var now = clock.UtcNow;
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        dbContext.ConfigurationAuditEvents.Add(new ConfigurationAuditEventRecord
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "UserAccount",
            AggregateId = aggregateId,
            EventType = eventType,
            ActorId = actorId,
            OccurredAt = now,
            PayloadJson = payloadJson,
            CorrelationId = correlationId
        });
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.CreateVersion7(),
            AggregateId = aggregateId,
            EventType = eventType,
            PayloadJson = payloadJson,
            OccurredAt = now
        });
    }

    private async Task SaveConcurrencySafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    private void ApplyConcurrency(UserAccountRecord record, string expectedETag)
    {
        var expected = DecodeETag(expectedETag);
        dbContext.Entry(record).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static void AddDescendants(
        Guid parentId,
        IReadOnlyCollection<LocationMasterRecord> locations,
        ISet<string> scopes)
    {
        foreach (var child in locations.Where(x => x.ParentId == parentId))
        {
            scopes.Add(child.Code);
            AddDescendants(child.Id, locations, scopes);
        }
    }

    private static (byte[] Salt, byte[] Hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return (salt, hash);
    }

    private static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();

    private static UserAccountRecord ToRecord(UserAccount account) => new()
    {
        SubjectId = account.SubjectId,
        UserName = account.UserName,
        NormalizedUserName = NormalizeUserName(account.UserName),
        DisplayName = account.DisplayName,
        Position = account.Position,
        Department = account.Department,
        IsActive = account.IsActive,
        Version = account.Version,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt,
        SecurityStamp = account.SecurityStamp
    };

    private static void Apply(UserAccountRecord record, UserAccount account)
    {
        record.DisplayName = account.DisplayName;
        record.Position = account.Position;
        record.Department = account.Department;
        record.IsActive = account.IsActive;
        record.Version = account.Version;
        record.UpdatedAt = account.UpdatedAt;
        record.SecurityStamp = account.SecurityStamp;
    }

    private async Task<DateTimeOffset?> ActiveLockAsync(string subjectId, CancellationToken cancellationToken)
    {
        var lockedUntil = await dbContext.UserCredentials.AsNoTracking()
            .Where(x => x.SubjectId == subjectId)
            .Select(x => x.LockedUntil)
            .SingleOrDefaultAsync(cancellationToken);
        return lockedUntil > clock.UtcNow ? lockedUntil : null;
    }

    private static StoredUserAccount ToStored(
        UserAccountRecord account,
        UserSignatureVersionRecord? signature,
        DateTimeOffset? lockedUntil = null) => new(
        UserAccount.Rehydrate(
            account.SubjectId,
            account.UserName,
            account.DisplayName,
            account.Position,
            account.Department,
            account.IsActive,
            account.Version,
            account.CreatedAt,
            account.UpdatedAt,
            account.SecurityStamp),
        EncodeETag(account.RowVersion),
        signature is null ? null : ToEntry(signature),
        lockedUntil);

    private static UserSignatureEntry ToEntry(UserSignatureVersionRecord signature) => new(
        signature.Id,
        signature.SubjectId,
        signature.Version,
        signature.MediaType,
        signature.Content,
        signature.Sha256,
        signature.UploadedBy,
        signature.UploadedAt,
        signature.IsActive);

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string EncodeETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    private static byte[] DecodeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidRequestException("concurrency.if_match_required", "Header If-Match wajib untuk perubahan pengguna.");
        }

        try
        {
            return Convert.FromBase64String(etag.Trim().Trim('"'));
        }
        catch (FormatException)
        {
            throw new InvalidRequestException("concurrency.if_match_invalid", "Header If-Match tidak valid.");
        }
    }
}
