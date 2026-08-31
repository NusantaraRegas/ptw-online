using System.Security.Cryptography;
using System.Text.Json;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class UserAuthorizationService(
    IUserAuthorizationStore store,
    ILocationMasterStore locationStore,
    IActorContext actorContext,
    IClock clock)
{
    public async Task<PagedResponse<UserAuthorizationResponse>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        var items = (await store.ListAsync(cancellationToken)).Select(ToResponse).ToArray();
        return new PagedResponse<UserAuthorizationResponse>(items, items.Length);
    }

    public async Task<UserAuthorizationResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        return ToResponse(await GetStoredAsync(id, cancellationToken));
    }

    public async Task<UserAuthorizationResponse> CreateAsync(
        UserAuthorizationDraftRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        var kind = ParseKind(request.Kind);
        await EnsureReferencesAsync(request, kind, false, cancellationToken);
        var entry = UserAuthorizationAssignment.CreateDraft(
            request.SubjectId,
            request.RoleCode,
            request.ActionCodes,
            request.LocationId,
            request.IncludeDescendants,
            request.RequiredCompetencyCodes,
            kind,
            request.SourceAuthorizationId,
            request.EffectiveFrom,
            request.EffectiveUntil,
            actor.Id,
            clock.UtcNow);
        return ToResponse(await store.AddAsync(entry, actor, correlationId, cancellationToken));
    }

    public async Task<UserAuthorizationResponse> UpdateDraftAsync(
        Guid id,
        UserAuthorizationDraftRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        var stored = await GetStoredAsync(id, cancellationToken);
        var kind = ParseKind(request.Kind);
        await EnsureReferencesAsync(request, kind, false, cancellationToken);
        stored.Entry.UpdateDraft(
            request.SubjectId,
            request.RoleCode,
            request.ActionCodes,
            request.LocationId,
            request.IncludeDescendants,
            request.RequiredCompetencyCodes,
            kind,
            request.SourceAuthorizationId,
            request.EffectiveFrom,
            request.EffectiveUntil,
            actor.Id,
            clock.UtcNow);
        return ToResponse(await store.UpdateAsync(
            stored.Entry,
            expectedETag,
            actor,
            correlationId,
            null,
            cancellationToken));
    }

    public Task<UserAuthorizationResponse> SubmitAsync(
        Guid id,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            "SubmitUserAuthorization",
            idempotencyKey,
            new { Id = id },
            expectedETag,
            correlationId,
            (entry, actor, now, _) =>
            {
                entry.SubmitForApproval(actor.Id, now);
                return Task.CompletedTask;
            },
            cancellationToken);

    public Task<UserAuthorizationResponse> ApproveAsync(
        Guid id,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            "ApproveUserAuthorization",
            idempotencyKey,
            new { Id = id },
            expectedETag,
            correlationId,
            async (entry, actor, now, token) =>
            {
                await EnsureApprovalReferencesAsync(entry, token);
                entry.Approve(actor.Id, now);
            },
            cancellationToken);

    public Task<UserAuthorizationResponse> ReturnForChangesAsync(
        Guid id,
        ReturnAuthorizationForChangesRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            "ReturnUserAuthorizationForChanges",
            idempotencyKey,
            new { Id = id, request.Reason },
            expectedETag,
            correlationId,
            (entry, actor, now, _) =>
            {
                entry.ReturnForChanges(actor.Id, request.Reason, now);
                return Task.CompletedTask;
            },
            cancellationToken);

    private async Task<UserAuthorizationResponse> ExecuteCommandAsync<TPayload>(
        Guid id,
        string operation,
        string idempotencyKey,
        TPayload payload,
        string expectedETag,
        string correlationId,
        Func<UserAuthorizationAssignment, Actor, DateTimeOffset, CancellationToken, Task> execute,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib untuk command transisi.");
        }

        var requestHash = Hash(payload);
        var prior = await store.FindCommandResultAsync(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return ToResponse(prior);
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        await execute(stored.Entry, actor, clock.UtcNow, cancellationToken);
        var command = new AuthorizationCommandContext(actor.Id, operation, idempotencyKey, requestHash);
        return ToResponse(await store.UpdateAsync(
            stored.Entry,
            expectedETag,
            actor,
            correlationId,
            command,
            cancellationToken));
    }

    private async Task EnsureReferencesAsync(
        UserAuthorizationDraftRequest request,
        AuthorizationAssignmentKind kind,
        bool requireApproved,
        CancellationToken cancellationToken)
    {
        if (request.LocationId is not null)
        {
            var location = await locationStore.FindAsync(request.LocationId.Value, cancellationToken)
                ?? throw new InvalidRequestException(
                    "authorization.location_not_found",
                    "Lokasi assignment tidak ditemukan.");
            if (requireApproved)
            {
                EnsureLocationCovers(location.Entry, request.EffectiveFrom, request.EffectiveUntil);
            }
        }

        if (kind == AuthorizationAssignmentKind.Delegation && request.SourceAuthorizationId is not null)
        {
            var source = await store.FindAsync(request.SourceAuthorizationId.Value, cancellationToken)
                ?? throw new InvalidRequestException(
                    "authorization.source_not_found",
                    "Assignment sumber delegasi tidak ditemukan.");
            if (requireApproved)
            {
                await EnsureDelegationDoesNotBroadenAsync(request, source.Entry, cancellationToken);
            }
        }
    }

    private Task EnsureApprovalReferencesAsync(
        UserAuthorizationAssignment entry,
        CancellationToken cancellationToken) =>
        EnsureReferencesAsync(
            new UserAuthorizationDraftRequest(
                entry.SubjectId,
                entry.RoleCode,
                entry.ActionCodes,
                entry.LocationId,
                entry.IncludeDescendants,
                entry.RequiredCompetencyCodes,
                entry.Kind.ToString(),
                entry.SourceAuthorizationId,
                entry.EffectiveFrom,
                entry.EffectiveUntil),
            entry.Kind,
            true,
            cancellationToken);

    private static void EnsureLocationCovers(
        LocationMasterEntry location,
        DateTimeOffset from,
        DateTimeOffset? until)
    {
        if (location.Status != LocationMasterStatus.Approved
            || from.ToUniversalTime() < location.EffectiveFrom
            || !PeriodEndIsCovered(until, location.EffectiveUntil))
        {
            throw new InvalidRequestException(
                "authorization.location_period_not_covered",
                "Lokasi harus disetujui dan periode efektifnya harus mencakup seluruh periode assignment.");
        }
    }

    private async Task EnsureDelegationDoesNotBroadenAsync(
        UserAuthorizationDraftRequest delegated,
        UserAuthorizationAssignment source,
        CancellationToken cancellationToken)
    {
        if (source.Status != AuthorizationAssignmentStatus.Approved
            || source.Kind != AuthorizationAssignmentKind.Direct)
        {
            throw new InvalidRequestException(
                "authorization.delegation_source_invalid",
                "Delegasi hanya dapat berasal dari assignment langsung yang telah disetujui.");
        }

        if (string.Equals(source.SubjectId, delegated.SubjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRequestException(
                "authorization.delegation_same_subject",
                "Pemberi dan penerima delegasi harus berbeda.");
        }

        if (!string.Equals(source.RoleCode, delegated.RoleCode, StringComparison.OrdinalIgnoreCase)
            || !IsSubset(delegated.ActionCodes, source.ActionCodes)
            || !IsSubset(delegated.RequiredCompetencyCodes, source.RequiredCompetencyCodes)
            || delegated.EffectiveFrom.ToUniversalTime() < source.EffectiveFrom
            || !PeriodEndIsCovered(delegated.EffectiveUntil, source.EffectiveUntil)
            || !await ScopeIsCoveredAsync(source, delegated, cancellationToken))
        {
            throw new InvalidRequestException(
                "authorization.delegation_broadens_scope",
                "Delegasi tidak boleh memperluas role, action, kompetensi, lokasi, atau periode sumber authority.");
        }
    }

    private async Task<bool> ScopeIsCoveredAsync(
        UserAuthorizationAssignment source,
        UserAuthorizationDraftRequest delegated,
        CancellationToken cancellationToken)
    {
        if (source.LocationId is null)
        {
            return true;
        }

        if (delegated.LocationId is null)
        {
            return false;
        }

        if (source.LocationId == delegated.LocationId)
        {
            return !delegated.IncludeDescendants || source.IncludeDescendants;
        }

        return source.IncludeDescendants
            && await IsDescendantAsync(delegated.LocationId.Value, source.LocationId.Value, cancellationToken);
    }

    private async Task<bool> IsDescendantAsync(
        Guid candidateId,
        Guid ancestorId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        Guid? currentId = candidateId;
        while (currentId is not null && visited.Add(currentId.Value))
        {
            var current = await locationStore.FindAsync(currentId.Value, cancellationToken);
            if (current?.Entry.ParentId == ancestorId)
            {
                return true;
            }

            currentId = current?.Entry.ParentId;
        }

        return false;
    }

    private Actor EnsureAdministrator()
    {
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Peran Administrator diperlukan untuk mengelola assignment otorisasi.");
        }

        return actor;
    }

    private async Task<StoredUserAuthorization> GetStoredAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Assignment otorisasi", id);

    private UserAuthorizationResponse ToResponse(StoredUserAuthorization stored)
    {
        var entry = stored.Entry;
        return new UserAuthorizationResponse(
            entry.Id,
            entry.SubjectId,
            entry.RoleCode,
            entry.ActionCodes,
            entry.LocationId,
            entry.IncludeDescendants,
            entry.RequiredCompetencyCodes,
            entry.Kind.ToString().ToUpperInvariant(),
            entry.SourceAuthorizationId,
            entry.EffectiveFrom,
            entry.EffectiveUntil,
            ToUpperSnakeCase(entry.Status.ToString()),
            entry.IsEffectiveAt(clock.UtcNow),
            entry.Version,
            entry.MakerId,
            entry.CheckerId,
            entry.ApprovedAt,
            entry.CreatedAt,
            entry.UpdatedAt,
            stored.ETag);
    }

    private static AuthorizationAssignmentKind ParseKind(string value) =>
        Enum.TryParse<AuthorizationAssignmentKind>(value, true, out var kind)
            ? kind
            : throw new InvalidRequestException(
                "authorization.invalid_kind",
                "Jenis assignment harus DIRECT atau DELEGATION.");

    private static bool IsSubset(IReadOnlyList<string> candidate, IReadOnlyList<string> source)
    {
        var sourceSet = source.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidate.All(sourceSet.Contains);
    }

    private static bool PeriodEndIsCovered(DateTimeOffset? candidate, DateTimeOffset? source) =>
        source is null || candidate is not null && candidate.Value.ToUniversalTime() <= source.Value;

    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ToUpperSnakeCase(string value) => string.Concat(
        value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{character}" : character.ToString()))
        .ToUpperInvariant();
}

public sealed class AuthorizationAssignmentResolver(
    IUserAuthorizationStore store,
    ILocationMasterStore locationStore) : IAuthorizationAssignmentResolver
{
    public async Task<AuthorizationResolution> ResolveAsync(
        string subjectId,
        string actionCode,
        Guid? locationId,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        var candidates = new List<UserAuthorizationAssignment>();
        foreach (var stored in await store.ListApprovedForSubjectAsync(subjectId, cancellationToken))
        {
            var entry = stored.Entry;
            if (entry.IsEffectiveAt(instant)
                && entry.ActionCodes.Contains(actionCode, StringComparer.OrdinalIgnoreCase)
                && await LocationMatchesAsync(entry, locationId, cancellationToken))
            {
                candidates.Add(entry);
            }
        }

        if (candidates.Count == 0)
        {
            return new AuthorizationResolution(false, "authorization.assignment_missing", [], []);
        }

        if (candidates.GroupBy(item => item.RoleCode, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return new AuthorizationResolution(false, "authorization.assignment_ambiguous", [], []);
        }

        return new AuthorizationResolution(
            true,
            "authorization.assignment_resolved",
            candidates.Select(item => item.Id).ToArray(),
            candidates.SelectMany(item => item.RequiredCompetencyCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private async Task<bool> LocationMatchesAsync(
        UserAuthorizationAssignment entry,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        if (entry.LocationId is null)
        {
            return true;
        }

        if (locationId is null)
        {
            return false;
        }

        if (entry.LocationId == locationId)
        {
            return true;
        }

        if (!entry.IncludeDescendants)
        {
            return false;
        }

        var visited = new HashSet<Guid>();
        Guid? currentId = locationId;
        while (currentId is not null && visited.Add(currentId.Value))
        {
            var current = await locationStore.FindAsync(currentId.Value, cancellationToken);
            if (current?.Entry.ParentId == entry.LocationId)
            {
                return true;
            }

            currentId = current?.Entry.ParentId;
        }

        return false;
    }
}
