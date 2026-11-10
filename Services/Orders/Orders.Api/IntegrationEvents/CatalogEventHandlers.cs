using Orders.Dal;
using Orders.Domain.Models;
using Platform.EventBus;
using Platform.EventBus.Events;

namespace Orders.Api.IntegrationEvents;

// Idempotent processing: the EventId is inserted into dbo.ProcessedEvents in the SAME transaction as the change.
// A second delivery of the same event fails on the primary key, the change is not applied twice.
// If the change fails, the EventId is rolled back too, so a retry processes the event again.
public abstract class CatalogEventHandler<TEvent>(IUnitOfWork uow, ILogger logger) : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
{
    protected IUnitOfWork Uow => uow;
    protected ILogger Logger => logger;

    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct: ct);
        try
        {
            if (!await uow.ProcessedEvents.TryAddAsync(@event.EventId, ct))
            {
                await uow.RollbackAsync(ct);
                logger.LogInformation("Duplicate {EventType} {EventId} skipped: already processed", typeof(TEvent).Name, @event.EventId);
                return;
            }

            await ApplyAsync(@event, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    protected abstract Task ApplyAsync(TEvent @event, CancellationToken ct);
}

// New ticket type: a new row of the local reference, the stock of Orders starts from the capacity
public sealed class TicketTypeCreatedHandler(IUnitOfWork uow, ILogger<TicketTypeCreatedHandler> logger)
    : CatalogEventHandler<TicketTypeCreatedEvent>(uow, logger)
{
    protected override async Task ApplyAsync(TicketTypeCreatedEvent e, CancellationToken ct)
    {
        if (e.Price <= 0 || e.Capacity < 0)
            throw new PermanentEventException($"Ticket type {e.TicketTypeId}: price {e.Price} and capacity {e.Capacity} are invalid");

        var added = await Uow.CatalogReplica.AddTicketTypeAsync(new TicketType
        {
            Id = e.TicketTypeId, EventId = e.CatalogEventId, EventTitle = e.EventTitle, EventStartsAt = e.EventStartsAt,
            Name = e.Name, Price = e.Price, Stock = e.Capacity
        }, e.OccurredAt, ct);

        Logger.LogInformation("Ticket type {TicketTypeId} of event {CatalogEventId} {Result} in the local reference",
            e.TicketTypeId, e.CatalogEventId, added ? "added" : "already exists");
    }
}

// New price only in the reference. OrderItems.UnitPrice of placed orders is a snapshot and is NOT updated.
public sealed class TicketTypePriceChangedHandler(IUnitOfWork uow, ILogger<TicketTypePriceChangedHandler> logger)
    : CatalogEventHandler<TicketTypePriceChangedEvent>(uow, logger)
{
    protected override async Task ApplyAsync(TicketTypePriceChangedEvent e, CancellationToken ct)
    {
        if (e.NewPrice <= 0)
            throw new PermanentEventException($"Ticket type {e.TicketTypeId}: price {e.NewPrice} is not positive");

        var applied = await Uow.CatalogReplica.ApplyPriceAsync(e.TicketTypeId, e.Name, e.NewPrice, e.OccurredAt, ct);
        if (applied)
            Logger.LogInformation("Ticket type {TicketTypeId} price {OldPrice} -> {NewPrice} applied to the local reference",
                e.TicketTypeId, e.OldPrice, e.NewPrice);
        else
            Logger.LogWarning("Price of ticket type {TicketTypeId} not applied: unknown ticket type or a newer change is already there",
                e.TicketTypeId);
    }
}

// The copy stays (placed orders refer to it) but cannot be sold any more
public sealed class TicketTypeDeletedHandler(IUnitOfWork uow, ILogger<TicketTypeDeletedHandler> logger)
    : CatalogEventHandler<TicketTypeDeletedEvent>(uow, logger)
{
    protected override async Task ApplyAsync(TicketTypeDeletedEvent e, CancellationToken ct)
    {
        var applied = await Uow.CatalogReplica.TakeOffSaleAsync(e.TicketTypeId, e.OccurredAt, ct);
        Logger.LogInformation("Ticket type {TicketTypeId} taken off sale: {Applied}", e.TicketTypeId, applied);
    }
}

public sealed class EventRenamedHandler(IUnitOfWork uow, ILogger<EventRenamedHandler> logger)
    : CatalogEventHandler<EventRenamedEvent>(uow, logger)
{
    protected override async Task ApplyAsync(EventRenamedEvent e, CancellationToken ct)
    {
        var rows = await Uow.CatalogReplica.ApplyEventTitleAsync(e.CatalogEventId, e.NewTitle, e.OccurredAt, ct);
        Logger.LogInformation("Event {CatalogEventId} renamed to {NewTitle}: {Rows} ticket types updated",
            e.CatalogEventId, e.NewTitle, rows);
    }
}

public sealed class EventRescheduledHandler(IUnitOfWork uow, ILogger<EventRescheduledHandler> logger)
    : CatalogEventHandler<EventRescheduledEvent>(uow, logger)
{
    protected override async Task ApplyAsync(EventRescheduledEvent e, CancellationToken ct)
    {
        var rows = await Uow.CatalogReplica.ApplyEventScheduleAsync(e.CatalogEventId, e.StartsAt, e.OccurredAt, ct);
        Logger.LogInformation("Event {CatalogEventId} rescheduled to {StartsAt}: {Rows} ticket types updated",
            e.CatalogEventId, e.StartsAt, rows);
    }
}
