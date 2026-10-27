using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

public interface IEventRepository : IGenericRepository<Event>
{
    Task<Event?> GetWithDetailsAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsBySlugAsync(string slug, CancellationToken ct = default);
}

// Eager Loading: event card with all related data in ONE SQL query
public class EventRepository(CatalogDbContext context) : GenericRepository<Event>(context), IEventRepository
{
    public Task<Event?> GetWithDetailsAsync(int id, CancellationToken ct = default)
        => Set.Include(e => e.Detail)
              .Include(e => e.Venue)
              .Include(e => e.TicketTypes)
              .Include(e => e.EventCategories).ThenInclude(ec => ec.Category)
              .FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken ct = default)
        => Set.AnyAsync(e => e.Slug == slug, ct);
}
