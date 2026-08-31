using System.Security.Cryptography;
using System.Text.Json;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class LocationMasterService(
    ILocationMasterStore store,
    IActorContext actorContext,
    IClock clock)
{
    public async Task<PagedResponse<LocationMasterResponse>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        var items = (await store.ListAsync(cancellationToken)).Select(ToResponse).ToArray();
        return new PagedResponse<LocationMasterResponse>(items, items.Length);
    }

    public async Task<LocationMasterResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        return ToResponse(await GetStoredAsync(id, cancellationToken));
    }

    public async Task<LocationMasterResponse> CreateAsync(
        LocationDraftRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        await EnsureParentGraphAsync(null, request.ParentId, cancellationToken);
        var entry = LocationMasterEntry.CreateDraft(
            request.Code,
            request.Name,
            request.ParentId,
            request.EffectiveFrom,
            request.EffectiveUntil,
            actor.Id,
            clock.UtcNow);
        return ToResponse(await store.AddAsync(entry, actor, correlationId, cancellationToken));
    }

    public async Task<LocationMasterResponse> UpdateDraftAsync(
        Guid id,
        LocationDraftRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        var stored = await GetStoredAsync(id, cancellationToken);
        await EnsureParentGraphAsync(id, request.ParentId, cancellationToken);
        stored.Entry.UpdateDraft(
            request.Code,
            request.Name,
            request.ParentId,
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

    public Task<LocationMasterResponse> SubmitAsync(
        Guid id,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            "SubmitLocationMaster",
            idempotencyKey,
            new { Id = id },
            expectedETag,
            correlationId,
            (entry, actor, now) => entry.SubmitForApproval(actor.Id, now),
            cancellationToken);

    public Task<LocationMasterResponse> ApproveAsync(
        Guid id,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            "ApproveLocationMaster",
            idempotencyKey,
            new { Id = id },
            expectedETag,
            correlationId,
            (entry, actor, now) => entry.Approve(actor.Id, now),
            cancellationToken);

    public Task<LocationMasterResponse> ReturnForChangesAsync(
        Guid id,
        ReturnLocationForChangesRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            "ReturnLocationMasterForChanges",
            idempotencyKey,
            new { Id = id, request.Reason },
            expectedETag,
            correlationId,
            (entry, actor, now) => entry.ReturnForChanges(actor.Id, request.Reason, now),
            cancellationToken);

    private async Task<LocationMasterResponse> ExecuteCommandAsync<TPayload>(
        Guid id,
        string operation,
        string idempotencyKey,
        TPayload payload,
        string expectedETag,
        string correlationId,
        Action<LocationMasterEntry, Actor, DateTimeOffset> execute,
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
        execute(stored.Entry, actor, clock.UtcNow);
        var command = new LocationCommandContext(actor.Id, operation, idempotencyKey, requestHash);
        return ToResponse(await store.UpdateAsync(
            stored.Entry,
            expectedETag,
            actor,
            correlationId,
            command,
            cancellationToken));
    }

    private async Task EnsureParentGraphAsync(
        Guid? entryId,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return;
        }

        var visited = new HashSet<Guid>();
        var currentId = parentId;
        while (currentId is not null)
        {
            if (currentId == entryId || !visited.Add(currentId.Value))
            {
                throw new InvalidRequestException(
                    "location.hierarchy_cycle",
                    "Hierarki lokasi tidak boleh membentuk siklus.");
            }

            var current = await store.FindAsync(currentId.Value, cancellationToken)
                ?? throw new InvalidRequestException(
                    "location.parent_not_found",
                    "Lokasi induk tidak ditemukan.");
            currentId = current.Entry.ParentId;
        }
    }

    private Actor EnsureAdministrator()
    {
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("Peran Administrator diperlukan untuk mengelola master lokasi.");
        }

        return actor;
    }

    private async Task<StoredLocationMaster> GetStoredAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Master lokasi", id);

    private LocationMasterResponse ToResponse(StoredLocationMaster stored)
    {
        var entry = stored.Entry;
        return new LocationMasterResponse(
            entry.Id,
            entry.Code,
            entry.Name,
            entry.ParentId,
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
