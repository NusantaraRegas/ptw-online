using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class PermitService(
    IPermitStore store,
    IActorContext actorContext,
    IClock clock,
    IPermitNumberGenerator numberGenerator)
{
    public async Task<PermitResponse> CreateAsync(PermitDraftRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        EnsureLocationScope(actor, request.LocationId);
        if (!string.Equals(actor.Id, request.SponsorId, StringComparison.OrdinalIgnoreCase) && !actor.Roles.Contains("Administrator"))
        {
            throw new InvalidRequestException("permit.sponsor_mismatch", "Sponsor hanya dapat membuat PTW untuk identitasnya sendiri.");
        }

        var permit = Permit.CreateDraft(request.ToDomain(), clock.UtcNow);
        return (await store.AddAsync(permit, actor, correlationId, cancellationToken)).ToResponse();
    }

    public async Task<PermitResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        return stored.ToResponse();
    }

    public async Task<PagedResponse<PermitResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        var sponsorFilter = actor.Roles.Overlaps(["Auditor", "Administrator", "HSEReviewer", "OperationsReviewer"])
            ? null
            : actor.Id;
        var items = (await store.ListAsync(sponsorFilter, cancellationToken))
            .Where(x => HasLocationScope(actor, x.Permit.Draft.LocationId))
            .Select(x => x.ToResponse())
            .ToArray();
        return new PagedResponse<PermitResponse>(items, items.Length);
    }

    public async Task<PermitResponse> UpdateDraftAsync(
        Guid id,
        PermitDraftRequest request,
        string expectedETag,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var stored = await GetStoredAsync(id, cancellationToken);
        var actor = actorContext.Current;
        EnsureSponsorOwnership(actor, stored.Permit);
        EnsureLocationScope(actor, request.LocationId);
        stored.Permit.UpdateDraft(request.ToDomain(), clock.UtcNow);
        return (await store.UpdateAsync(stored.Permit, expectedETag, actor, correlationId, null, cancellationToken)).ToResponse();
    }

    public async Task<PermitResponse> SubmitAsync(
        Guid id,
        SubmitPermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException("idempotency.required", "Header Idempotency-Key wajib untuk command transisi.");
        }

        var actor = actorContext.Current;
        var requestHash = Hash(request);
        const string operation = "SubmitPermit";
        var prior = await store.FindIdempotentResultAsync(actor.Id, operation, idempotencyKey, requestHash, cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureSponsorOwnership(actor, stored.Permit);
        var readiness = new SubmissionReadiness(
            request.ESimiEligible,
            request.RulesEvaluated,
            request.RequiredDocumentsSafe,
            request.MissingRequirements);
        stored.Permit.Submit(numberGenerator.Generate(clock.UtcNow), readiness, clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(stored.Permit, expectedETag, actor, correlationId, idempotency, cancellationToken)).ToResponse();
    }

    private async Task<StoredPermit> GetStoredAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) ?? throw new ResourceNotFoundException("Permit", id);

    private static void EnsureSponsorOrAdmin(Actor actor)
    {
        if (!actor.Roles.Overlaps(["Sponsor", "Administrator"]))
        {
            throw new UnauthorizedAccessException("Peran Sponsor diperlukan untuk menyusun PTW.");
        }
    }

    private static void EnsureSponsorOwnership(Actor actor, Permit permit)
    {
        EnsureSponsorOrAdmin(actor);
        if (!string.Equals(actor.Id, permit.Draft.SponsorId, StringComparison.OrdinalIgnoreCase)
            && !actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException("PTW berada di luar kepemilikan Sponsor.");
        }
        EnsureLocationScope(actor, permit.Draft.LocationId);
    }

    private static void EnsureLocationScope(Actor actor, string locationId)
    {
        if (!HasLocationScope(actor, locationId))
        {
            throw new UnauthorizedAccessException("Lokasi PTW berada di luar cakupan otorisasi pengguna.");
        }
    }

    private static bool HasLocationScope(Actor actor, string locationId) =>
        actor.LocationScopes.Contains("*") || actor.LocationScopes.Contains(locationId);

    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
