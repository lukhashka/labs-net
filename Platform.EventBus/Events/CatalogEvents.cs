namespace Platform.EventBus.Events;

// Facts published by the Catalog (owner of events and ticket types), names in the past tense.
// Every event is self-contained: a consumer never calls the Catalog for more data.
// Who keeps which copy is defined by the duplication policy of Lab 01 (README, "Політика дублювання").
// CatalogEventId is the id of the event (concert) in the Catalog; EventId of the base record is the id of the message.

// Orders: a new row of the local reference TicketTypes; Stock starts from Capacity
public record TicketTypeCreatedEvent(
    int TicketTypeId, int CatalogEventId, string EventTitle, DateTime EventStartsAt,
    string Name, decimal Price, int Capacity) : IntegrationEvent;

// Orders: new price in TicketTypes. OrderItems.UnitPrice of placed orders stays: it is the price the client paid.
public record TicketTypePriceChangedEvent(
    int TicketTypeId, int CatalogEventId, string Name, decimal OldPrice, decimal NewPrice) : IntegrationEvent;

// Orders: the copy cannot be removed (placed orders refer to it), it is taken off sale (Stock = 0)
public record TicketTypeDeletedEvent(int TicketTypeId, int CatalogEventId) : IntegrationEvent;

// Orders: TicketTypes.EventTitle; Reviews: eventTitle in every review and discussion of the event
public record EventRenamedEvent(int CatalogEventId, string OldTitle, string NewTitle) : IntegrationEvent;

// Orders: TicketTypes.EventStartsAt
public record EventRescheduledEvent(int CatalogEventId, DateTime StartsAt, DateTime EndsAt) : IntegrationEvent;

public static class CatalogEvents
{
    public const string Exchange = "catalog-events";

    // routing keys of the topic exchange: <entity>.<what happened>
    public const string TicketTypeCreated = "ticket-type.created";
    public const string TicketTypePriceChanged = "ticket-type.price_changed";
    public const string TicketTypeDeleted = "ticket-type.deleted";
    public const string EventRenamed = "event.renamed";
    public const string EventRescheduled = "event.rescheduled";
}
