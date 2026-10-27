using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

public interface IEventCategoryRepository : IGenericRepository<EventCategory>
{
    Task<IReadOnlyList<Event>> GetRecentlyAddedAsync(int categoryId, DateTime since, CancellationToken ct = default);
}

// LINQ to Entities M:N: query through the join entity and its own field AddedAt
public class EventCategoryRepository(CatalogDbContext context)
    : GenericRepository<EventCategory>(context), IEventCategoryRepository
{
    public async Task<IReadOnlyList<Event>> GetRecentlyAddedAsync(
        int categoryId, DateTime since, CancellationToken ct = default)
        => await Set
            .Where(ec => ec.CategoryId == categoryId && ec.AddedAt >= since)
            .OrderByDescending(ec => ec.AddedAt)
            .Select(ec => ec.Event)
            .AsNoTracking()
            .ToListAsync(ct);
}
