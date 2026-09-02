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
    IPermitNumberGenerator numberGenerator,
    IOperationalPolicyGate operationalPolicyGate)
{
    private const string HsseValidatorRole = "HSSEValidator";
    private const string GasDistributionValidatorRole = "GasDistributionValidator";
    private const string AreaOwnerApproverRole = "AreaOwnerApprover";
    private const string IssuingAuthorityRole = "IssuingAuthority";

    public async Task<PermitResponse> CreateAsync(PermitDraftRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        EnsureSponsorOrAdmin(actor);
        EnsureLocationScope(actor, request.LocationId);
        if (!string.Equals(actor.Id, request.SponsorId, StringComparison.OrdinalIgnoreCase) && !actor.Roles.Contains("Administrator"))
        {
            throw new InvalidRequestException("permit.sponsor_mismatch", "Sponsor hanya dapat membuat PTW untuk identitasnya sendiri.");
        }

        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.CreateDraft,
            request.LocationId,
            cancellationToken);
        var permit = Permit.CreateDraft(request.ToDomain(), clock.UtcNow);
        return (await store.AddAsync(
            permit,
            actor,
            correlationId,
            authorizationEvidence,
            cancellationToken)).ToResponse();
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
        var sponsorFilter = actor.Roles.Overlaps(
            [
                "Auditor",
                "Administrator",
                HsseValidatorRole,
                GasDistributionValidatorRole,
                AreaOwnerApproverRole,
                IssuingAuthorityRole
            ])
            ? null
            : actor.Id;
        var items = (await store.ListAsync(sponsorFilter, cancellationToken))
            .Where(x => HasLocationScope(actor, x.Permit.Draft.LocationId))
            .Select(x => x.ToResponse())
            .ToArray();
        return new PagedResponse<PermitResponse>(items, items.Length);
    }

    public async Task<PagedResponse<PermitActivityResponse>> ListActivityAsync(
        Guid id,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        EnsureValidPage(offset, limit);
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        var page = await store.ListActivityAsync(id, offset, limit, cancellationToken);
        var items = page.Items.Select(entry => new PermitActivityResponse(
            entry.Sequence,
            entry.EventType,
            entry.ActorId,
            entry.OccurredAt,
            JsonSerializer.Deserialize<JsonElement>(entry.PayloadJson),
            entry.CorrelationId)).ToArray();
        return new PagedResponse<PermitActivityResponse>(items, page.Count);
    }

    public async Task<PagedResponse<PermitVersionResponse>> ListVersionsAsync(
        Guid id,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        EnsureValidPage(offset, limit);
        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actorContext.Current, stored.Permit.Draft.LocationId);
        var page = await store.ListVersionsAsync(id, offset, limit, cancellationToken);
        var items = page.Items.Select(entry => new PermitVersionResponse(
            entry.Version,
            entry.Draft.ToRequest(),
            entry.ContentHash,
            entry.CreatedAt,
            entry.CreatedBy)).ToArray();
        return new PagedResponse<PermitVersionResponse>(items, page.Count);
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
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.UpdateDraft,
            request.LocationId,
            cancellationToken);
        stored.Permit.UpdateDraft(request.ToDomain(), clock.UtcNow);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            null,
            authorizationEvidence,
            cancellationToken)).ToResponse();
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
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            PermitPolicyOperations.Submit,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        var readiness = new SubmissionReadiness(
            request.ESimiEligible,
            request.RulesEvaluated,
            request.RequiredDocumentsSafe,
            request.MissingRequirements);
        stored.Permit.Submit(numberGenerator.Generate(clock.UtcNow), readiness, clock.UtcNow);
        stored.Permit.StartReview(clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
    }

    public Task<PermitResponse> EndorseHsseValidationAsync(
        Guid id,
        EndorsePermitValidationRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ValidateHsse,
            [HsseValidatorRole],
            (permit, actor, now) => permit.EndorseValidation(
                PermitValidationKind.Hsse,
                actor.Id,
                request.Statement,
                now),
            cancellationToken);

    public Task<PermitResponse> EndorseGasDistributionValidationAsync(
        Guid id,
        EndorsePermitValidationRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.ValidateGasDistribution,
            [GasDistributionValidatorRole],
            (permit, actor, now) => permit.EndorseValidation(
                PermitValidationKind.GasDistribution,
                actor.Id,
                request.Statement,
                now),
            cancellationToken);

    public Task<PermitResponse> ApproveAsync(
        Guid id,
        ApprovePermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Approve,
            [AreaOwnerApproverRole],
            (permit, actor, now) => permit.Approve(actor.Id, request.Statement, now),
            cancellationToken);

    public Task<PermitResponse> IssueAsync(
        Guid id,
        IssuePermitRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(
            id,
            request,
            expectedETag,
            idempotencyKey,
            correlationId,
            PermitPolicyOperations.Issue,
            [IssuingAuthorityRole],
            (permit, actor, now) => permit.OpenWorkPeriod(
                new FieldIssueReadiness(
                    request.ESimiEligible,
                    request.LocationVerified,
                    request.ToolboxTalkComplete,
                    request.PersonnelAcknowledged,
                    request.PpeAndControlsVerified,
                    request.IsolationVerified,
                    request.SimopsVerified,
                    request.GasTestSatisfied,
                    request.HasUnresolvedSuspension),
                actor.Id,
                now),
            cancellationToken);

    private async Task<PermitResponse> ExecuteCommandAsync<TRequest>(
        Guid id,
        TRequest request,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        string operation,
        IReadOnlyCollection<string> allowedRoles,
        Action<Permit, Actor, DateTimeOffset> execute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib untuk command transisi.");
        }

        var actor = actorContext.Current;
        EnsureAnyRole(actor, allowedRoles);
        var requestHash = Hash(request);
        var prior = await store.FindIdempotentResultAsync(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return prior.ToResponse();
        }

        var stored = await GetStoredAsync(id, cancellationToken);
        EnsureLocationScope(actor, stored.Permit.Draft.LocationId);
        var authorizationEvidence = await operationalPolicyGate.AuthorizePermitCommandAsync(
            actor,
            operation,
            stored.Permit.Draft.LocationId,
            cancellationToken);
        execute(stored.Permit, actor, clock.UtcNow);
        var idempotency = new IdempotencyContext(actor.Id, operation, idempotencyKey, requestHash);
        return (await store.UpdateAsync(
            stored.Permit,
            expectedETag,
            actor,
            correlationId,
            idempotency,
            authorizationEvidence,
            cancellationToken)).ToResponse();
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

    private static void EnsureAnyRole(Actor actor, IReadOnlyCollection<string> allowedRoles)
    {
        if (!actor.Roles.Overlaps(allowedRoles))
        {
            throw new UnauthorizedAccessException(
                $"Aksi memerlukan salah satu role: {string.Join(", ", allowedRoles)}.");
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

    private static void EnsureValidPage(int offset, int limit)
    {
        if (offset < 0)
        {
            throw new InvalidRequestException("pagination.invalid_offset", "Offset tidak boleh negatif.");
        }

        if (limit is < 1 or > 100)
        {
            throw new InvalidRequestException("pagination.invalid_limit", "Limit harus berada antara 1 dan 100.");
        }
    }

    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
