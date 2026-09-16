using Ptw.Application;

namespace Ptw.Worker;

/// <summary>
/// Renders queued official permit packages. Hosted separately from <see cref="OutboxWorker"/> so a slow
/// or failing render never blocks integration dispatch.
/// </summary>
public sealed class PrintPackageRenderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PrintPackageRenderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    private static readonly Action<ILogger, Exception?> LogCycleFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2100, "PrintPackageRenderCycleFailed"),
            "Print package render cycle failed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<PrintPackageRenderExecutor>();
                claimed = await executor.RenderNextAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCycleFailure(logger, exception);
            }

            // Drain the queue back to back; only idle when there was nothing to claim.
            if (!claimed)
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
