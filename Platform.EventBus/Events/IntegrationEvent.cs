namespace Platform.EventBus.Events;

// Base of every integration event. A record: immutable, compared by value.
// EventId makes consumers idempotent, CorrelationId links the event to the HTTP request that caused it (Lab 05).
public abstract record IntegrationEvent
{
    // Increased only for an incompatible change. New fields are added, existing ones are never renamed or removed.
    public const int SchemaVersion = 1;

    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}
