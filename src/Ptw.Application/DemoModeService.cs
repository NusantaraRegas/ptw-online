using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ptw.Contracts;

namespace Ptw.Application;

public sealed class DemoModeService(
    IDemoModeStore store,
    IActorContext actorContext)
{
    public async Task<DemoModeResponse> GetPublicAsync(CancellationToken cancellationToken) =>
        Map(await store.GetAsync(cancellationToken));

    public async Task<DemoModeResponse> GetAdminAsync(CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        return Map(await store.GetAsync(cancellationToken));
    }

    public async Task<DemoModeResponse> SetAsync(
        bool enabled,
        string expectedETag,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = EnsureAdministrator();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidRequestException(
                "idempotency.required",
                "Header Idempotency-Key wajib untuk mengubah mode demo.");
        }

        var operation = enabled ? "EnableDemoMode" : "DisableDemoMode";
        var requestHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Enabled = enabled }))));
        var prior = await store.FindCommandResultAsync(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (prior is not null)
        {
            return Map(prior);
        }

        var command = new DemoModeCommandContext(
            actor.Id,
            operation,
            idempotencyKey,
            requestHash);
        return Map(await store.SetAsync(
            enabled,
            expectedETag,
            actor,
            correlationId,
            command,
            cancellationToken));
    }

    private Actor EnsureAdministrator()
    {
        var actor = actorContext.Current;
        if (!actor.Roles.Contains("Administrator"))
        {
            throw new UnauthorizedAccessException(
                "Peran Administrator diperlukan untuk mengelola pengaturan aplikasi.");
        }

        return actor;
    }

    private static DemoModeResponse Map(StoredDemoModeSetting stored) => new(
        stored.Enabled,
        stored.UpdatedAt,
        stored.UpdatedBy,
        stored.ETag);
}

