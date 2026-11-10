using Catalog.Dal.Outbox;
using Catalog.Dal.Repositories;
using Catalog.Domain.Entities;
using Platform.EventBus.Events;

namespace Catalog.Dal;

public interface IUnitOfWork
{
    IEventRepository Events { get; }
    ICategoryRepository Categories { get; }
    IEventCategoryRepository EventCategories { get; }
    ITicketTypeRepository TicketTypes { get; }
    IGenericRepository<Venue> Venues { get; }

    // the only place where changes go to the DB — one transaction per business operation
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // Lab 08: the event is saved by the same SaveChanges as the change itself (transactional outbox)
    void AddIntegrationEvent<T>(T @event, string routingKey) where T : IntegrationEvent;

    // For an operation that needs two SaveChanges, e.g. the id of a new row is part of the event
    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly CatalogDbContext _context;

    public UnitOfWork(CatalogDbContext context)
    {
        _context = context;
        Events = new EventRepository(context);
        Categories = new CategoryRepository(context);
        EventCategories = new EventCategoryRepository(context);
        TicketTypes = new TicketTypeRepository(context);
        Venues = new GenericRepository<Venue>(context);
    }

    public IEventRepository Events { get; }
    public ICategoryRepository Categories { get; }
    public IEventCategoryRepository EventCategories { get; }
    public ITicketTypeRepository TicketTypes { get; }
    public IGenericRepository<Venue> Venues { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public void AddIntegrationEvent<T>(T @event, string routingKey) where T : IntegrationEvent
        => _context.OutboxMessages.Add(OutboxMessage.Create(@event, CatalogEvents.Exchange, routingKey));

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        await operation();
        await transaction.CommitAsync(ct);          // without the commit the transaction is rolled back on dispose
    }
}
