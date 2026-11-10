using System.Diagnostics.Metrics;
using FluentValidation;
using MediatR;
using Platform.EventBus;
using Platform.EventBus.Events;
using RabbitMQ.Client;
using Reviews.Application.Catalog.Commands.SyncEventTitle;

namespace Reviews.WebApi.IntegrationEvents;

// Adapter of the presentation layer: the event becomes a command of the Application layer, like an HTTP request does
public sealed class EventRenamedHandler(ISender mediator) : IIntegrationEventHandler<EventRenamedEvent>
{
    public async Task HandleAsync(EventRenamedEvent e, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new SyncEventTitleCommand(e.EventId, e.CatalogEventId, e.NewTitle, e.OccurredAt), ct);
        }
        catch (ValidationException ex)
        {
            // invalid data will be invalid on every retry as well
            throw new PermanentEventException($"Rename of event {e.CatalogEventId} is invalid: {ex.Message}");
        }
    }
}

// Queue reviews.catalog-sync: Reviews copies only the title of the event
public sealed class CatalogEventsConsumer : RabbitMqConsumer
{
    public CatalogEventsConsumer(IConnection connection, IServiceScopeFactory scopes,
        ILogger<CatalogEventsConsumer> logger, IMeterFactory meters)
        : base(connection, scopes, logger, meters)
        => Handle<EventRenamedEvent, EventRenamedHandler>();

    protected override QueueDefinition Queue { get; } =
        new(CatalogEvents.Exchange, "reviews.catalog-sync", CatalogEvents.EventRenamed);

    public static void Register(IServiceCollection services)
    {
        services.AddScoped<EventRenamedHandler>();
        services.AddHostedService<CatalogEventsConsumer>();
    }
}
