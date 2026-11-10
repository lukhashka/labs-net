using System.Diagnostics.Metrics;
using Platform.EventBus;
using Platform.EventBus.Events;
using RabbitMQ.Client;

namespace Orders.Api.IntegrationEvents;

// Queue orders.catalog-sync: all changes of ticket types and events that Orders keeps a copy of
public sealed class CatalogEventsConsumer : RabbitMqConsumer
{
    public CatalogEventsConsumer(IConnection connection, IServiceScopeFactory scopes,
        ILogger<CatalogEventsConsumer> logger, IMeterFactory meters)
        : base(connection, scopes, logger, meters)
    {
        Handle<TicketTypeCreatedEvent, TicketTypeCreatedHandler>();
        Handle<TicketTypePriceChangedEvent, TicketTypePriceChangedHandler>();
        Handle<TicketTypeDeletedEvent, TicketTypeDeletedHandler>();
        Handle<EventRenamedEvent, EventRenamedHandler>();
        Handle<EventRescheduledEvent, EventRescheduledHandler>();
    }

    protected override QueueDefinition Queue { get; } =
        new(CatalogEvents.Exchange, "orders.catalog-sync", "ticket-type.*", "event.*");

    public static void Register(IServiceCollection services)
    {
        services.AddScoped<TicketTypeCreatedHandler>();
        services.AddScoped<TicketTypePriceChangedHandler>();
        services.AddScoped<TicketTypeDeletedHandler>();
        services.AddScoped<EventRenamedHandler>();
        services.AddScoped<EventRescheduledHandler>();
        services.AddHostedService<CatalogEventsConsumer>();
    }
}
