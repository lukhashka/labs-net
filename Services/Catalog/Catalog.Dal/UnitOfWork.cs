using Catalog.Dal.Repositories;
using Catalog.Domain.Entities;

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
}
