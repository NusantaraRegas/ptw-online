using Microsoft.EntityFrameworkCore;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Worker;

public sealed class OutboxWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogDispatchFailure =
        LoggerMessage.Define(LogLevel.Error, new EventId(2000, "OutboxDispatchFailed"), "Outbox dispatch cycle failed");
    private static readonly Action<ILogger, Guid, string, Guid, Exception?> LogDispatch =
        LoggerMessage.Define<Guid, string, Guid>(
            LogLevel.Information,
            new EventId(2001, "OutboxMessageDispatched"),
            "Domain event ready for integration dispatch. EventId={EventId} EventType={EventType} PermitId={PermitId}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDispatchFailure(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var now = DateTimeOffset.UtcNow;
        var messages = await db.OutboxMessages
            .Where(x => x.ProcessedAt == null && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.OccurredAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            LogDispatch(logger, message.Id, message.EventType, message.AggregateId, null);
            message.ProcessedAt = now;
            message.Attempts++;
        }

        if (messages.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
