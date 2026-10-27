using Ardalis.Specification;
using Catalog.Bll.Dtos;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Specifications;

// Search by title, category, date range, price from/to, sorting startsAt|title|price, paging.
// The spec doesn't know about EF — repository applies it with WithSpecification().
public class EventsFilterSpec : Specification<Event>
{
    public EventsFilterSpec(EventQuery q, int page, int pageSize)
    {
        Query.AsNoTracking();
        Query.Include(e => e.TicketTypes);

        if (!string.IsNullOrWhiteSpace(q.Search))
            Query.Where(e => e.Title.Contains(q.Search));
        if (q.CategoryId is not null)
            Query.Where(e => e.EventCategories.Any(ec => ec.CategoryId == q.CategoryId));
        if (q.From is not null)
            Query.Where(e => e.StartsAt >= q.From);
        if (q.To is not null)
            Query.Where(e => e.StartsAt <= q.To);
        if (q.MinPrice is not null)
            Query.Where(e => e.TicketTypes.Any(t => t.Price >= q.MinPrice));
        if (q.MaxPrice is not null)
            Query.Where(e => e.TicketTypes.Any(t => t.Price <= q.MaxPrice));

        var desc = q.SortDir == "desc";
        switch (q.SortBy.ToLowerInvariant())
        {
            case "title":
                if (desc) Query.OrderByDescending(e => e.Title); else Query.OrderBy(e => e.Title);
                break;
            case "price":
                if (desc) Query.OrderByDescending(e => e.TicketTypes.Min(t => (decimal?)t.Price));
                else Query.OrderBy(e => e.TicketTypes.Min(t => (decimal?)t.Price));
                break;
            default:
                if (desc) Query.OrderByDescending(e => e.StartsAt); else Query.OrderBy(e => e.StartsAt);
                break;
        }

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
