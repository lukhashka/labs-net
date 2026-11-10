using System.Text;
using Catalog.Dal;
using Microsoft.EntityFrameworkCore;
using Platform.EventBus;

namespace Catalog.Api.IntegrationEvents;

// Second half of the transactional outbox: sends committed events from OutboxMessages to RabbitMQ.
// Rows go out in the order they were written. A row is marked as published only after the broker confirmed it,
// so a crash between publish and mark gives a duplicate, never a lost event (consumers are idempotent).
public sealed class OutboxPublisher(IServiceScopeFactory scopes, IEventPublisher publisher, ILogger<OutboxPublisher> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PauseAfterFailure = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = Interval;
            try
            {
                if (!await PublishBatchAsync(stoppingToken))
                    delay = PauseAfterFailure;          // the broker is down: do not hammer it
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox could not be read, next try in {Seconds} s", PauseAfterFailure.TotalSeconds);
                delay = PauseAfterFailure;
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // false: a message failed, the rest of the batch waits so the order of events is kept
    private async Task<bool> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var batch = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(new OutgoingMessage(
                    message.Id, message.EventType, message.Exchange, message.RoutingKey,
                    Encoding.UTF8.GetBytes(message.Payload), message.CorrelationId, message.OccurredAt,
                    TracePropagation.ParseTraceParent(message.TraceParent)), ct);   // the trace of the HTTP request goes on

                message.MarkPublished();
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message);
                await db.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning(ex, "Outbox message {EventType} {EventId} not published, attempt {Attempt}",
                    message.EventType, message.Id, message.Attempts);
                return false;
            }
        }

        return true;
    }
}
