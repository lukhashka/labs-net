using System.Text.Json;
using Platform.EventBus;
using Platform.EventBus.Events;

namespace Catalog.Dal.Outbox;

// Transactional outbox: the event is a row of CatalogDb, written by the same SaveChanges as the business change.
// If the transaction fails there is no event; if the application crashes after the commit, the event is not lost:
// OutboxPublisher (Catalog.Api/IntegrationEvents) sends it to RabbitMQ later. eShop does the same (IntegrationEventLogEF).
public class OutboxMessage
{
    private OutboxMessage() { }                       // for EF Core

    public Guid Id { get; private set; }              // = EventId
    public string EventType { get; private set; } = "";
    public string Exchange { get; private set; } = "";
    public string RoutingKey { get; private set; } = "";
    public string Payload { get; private set; } = "";
    public string? CorrelationId { get; private set; }
    public string? TraceParent { get; private set; }  // the trace of the HTTP request continues in the publisher
    public DateTime OccurredAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create<T>(T @event, string exchange, string routingKey) where T : IntegrationEvent => new()
    {
        Id = @event.EventId,
        EventType = typeof(T).Name,
        Exchange = exchange,
        RoutingKey = routingKey,
        Payload = JsonSerializer.Serialize(@event, JsonDefaults.Options),
        CorrelationId = @event.CorrelationId,
        TraceParent = TracePropagation.CurrentTraceParent(),
        OccurredAt = @event.OccurredAt
    };

    public void MarkPublished() => PublishedAt = DateTime.UtcNow;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}
