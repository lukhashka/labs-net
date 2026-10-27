using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

public interface ITicketTypeRepository : IGenericRepository<TicketType>
{
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);

    Task<(IReadOnlyList<TicketType> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, int? eventId, string? search, decimal? minPrice, decimal? maxPrice,
        string sortBy, string sortDir, CancellationToken ct = default);
}

public class TicketTypeRepository(CatalogDbContext context) : GenericRepository<TicketType>(context), ITicketTypeRepository
{
    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default)
        => Set.AnyAsync(t => t.Code == code, ct);

    // filtering, sorting and Skip/Take are built BEFORE ToListAsync -> executed by SQL Server
    public async Task<(IReadOnlyList<TicketType> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, int? eventId, string? search, decimal? minPrice, decimal? maxPrice,
        string sortBy, string sortDir, CancellationToken ct = default)
    {
        IQueryable<TicketType> query = Set.AsNoTracking();

        if (eventId is not null) query = query.Where(t => t.EventId == eventId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search) || t.Code.Contains(search));
        if (minPrice is not null) query = query.Where(t => t.Price >= minPrice);
        if (maxPrice is not null) query = query.Where(t => t.Price <= maxPrice);

        // only allowed sort fields
        query = (sortBy.ToLowerInvariant(), sortDir.ToLowerInvariant()) switch
        {
            ("price", "desc") => query.OrderByDescending(t => t.Price),
            ("price", _) => query.OrderBy(t => t.Price),
            ("code", "desc") => query.OrderByDescending(t => t.Code),
            ("code", _) => query.OrderBy(t => t.Code),
            (_, "desc") => query.OrderByDescending(t => t.Name),
            _ => query.OrderBy(t => t.Name)
        };

        var total = await query.CountAsync(ct);                                  // COUNT(*) in SQL
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)       // OFFSET ... FETCH
                               .ToListAsync(ct);
        return (items, total);
    }
}
