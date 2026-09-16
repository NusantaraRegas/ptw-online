namespace Ptw.Application;

/// <summary>
/// Renders one queued print package per call. Lives in the application layer so the retry and backoff
/// behaviour is testable without hosting the Worker.
/// </summary>
/// <remarks>
/// A render failure must never unwind the issuance decision (PRD FR-ISS-003). The executor only ever
/// touches the generated-document and snapshot render state, never the permit aggregate.
/// </remarks>
public sealed class PrintPackageRenderExecutor(
    IPrintPackageStore store,
    IPrintPackageRenderer renderer,
    IGeneratedDocumentStorage storage,
    IClock clock)
{
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(30);

    /// <summary>Returns true when a package was claimed, whether or not the render succeeded.</summary>
    public async Task<bool> RenderNextAsync(CancellationToken cancellationToken)
    {
        if (!renderer.IsAvailable)
        {
            return false;
        }

        var job = await store.ClaimNextAsync(clock.UtcNow, cancellationToken);
        if (job is null)
        {
            return false;
        }

        try
        {
            var snapshot = PrintPackageService.ParseSnapshot(job.SnapshotJson);
            var result = renderer.Render(
                new PrintPackageRenderRequest(snapshot, job.SnapshotHash, Watermark: false));
            var stored = await storage.StoreAsync(job.GeneratedDocumentId, result.Content, cancellationToken);
            await store.CompleteRenderAsync(
                job.GeneratedDocumentId,
                stored,
                result.MediaType,
                result.RendererVersion,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await store.FailRenderAsync(
                job.GeneratedDocumentId,
                exception.Message,
                clock.UtcNow.Add(BackoffFor(job.Attempts + 1)),
                cancellationToken);
        }

        return true;
    }

    /// <summary>Exponential backoff, capped so a persistently failing render still retries periodically.</summary>
    internal static TimeSpan BackoffFor(int attempts)
    {
        var minutes = Math.Pow(2, Math.Clamp(attempts, 1, 10));
        var delay = TimeSpan.FromMinutes(minutes);
        return delay > MaximumBackoff ? MaximumBackoff : delay;
    }
}
